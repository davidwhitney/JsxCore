using System.Text.Json;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

[Trait("Category", "Network")]
public class LockFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-lock-" + Guid.NewGuid().ToString("n")[..8]);

    private NativePackageManager Manager() => new(report: _ => { });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private JsonDocument Install(params PackageRequest[] packages)
    {
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);
        Manager().Add(_root, packages).Succeeded.ShouldBeTrue();
        return JsonDocument.Parse(File.ReadAllText(LockFile.PathIn(_root)));
    }

    [Fact]
    public void Write_AfterInstalling_ProducesLockfileVersionThree()
    {
        var lockFile = Install(new PackageRequest("classnames", "^2.5.1")).RootElement;

        lockFile.GetProperty("lockfileVersion").GetInt32().ShouldBe(3);
        lockFile.GetProperty("requires").GetBoolean().ShouldBeTrue();
        lockFile.GetProperty("packages").TryGetProperty("", out _).ShouldBeTrue();
    }

    [Fact]
    public void Write_ForEachPackage_RecordsWhatIsNeededToFetchItAgain()
    {
        var packages = Install(new PackageRequest("classnames", "^2.5.1"))
            .RootElement.GetProperty("packages").GetProperty("node_modules/classnames");

        packages.GetProperty("version").GetString().ShouldNotBeNull().ShouldStartWith("2.");
        packages.GetProperty("resolved").GetString().ShouldNotBeNull().ShouldContain("classnames-");
        packages.GetProperty("integrity").GetString().ShouldNotBeNull().ShouldStartWith("sha");
    }

    [Fact]
    public void Write_RootEntry_MirrorsTheManifestSoNpmAcceptsIt()
    {
        // npm refuses to install when the lock file and package.json disagree about what is declared.
        var root = Install(new PackageRequest("classnames", "^2.5.1"))
            .RootElement.GetProperty("packages").GetProperty("");

        root.GetProperty("dependencies").GetProperty("classnames").GetString().ShouldBe("^2.5.1");
    }

    [Fact]
    public void Write_PackageIsPlatformSpecific_RecordsEveryPlatformNotJustThisOne()
    {
        // A lock file that only described this machine would install no compiler anywhere else.
        var packages = Install(new PackageRequest("typescript", "^7.0.0", Development: true))
            .RootElement.GetProperty("packages");

        var platformEntries = packages.EnumerateObject()
            .Where(p => p.Name.Contains("@typescript/typescript-"))
            .ToList();

        platformEntries.Count.ShouldBeGreaterThan(5);
        platformEntries.ShouldContain(p => p.Name.Contains("linux"));
        platformEntries.ShouldContain(p => p.Name.Contains("win32"));
        platformEntries.ShouldAllBe(p => p.Value.GetProperty("optional").GetBoolean());
    }

    [Fact]
    public void Write_PackageCameFromDevDependencies_IsMarkedDev()
    {
        var entry = Install(new PackageRequest("ms", "^2.1.3", Development: true))
            .RootElement.GetProperty("packages").GetProperty("node_modules/ms");

        entry.GetProperty("dev").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Write_RunTwiceOverTheSameInputs_ProducesIdenticalBytes()
    {
        Install(new PackageRequest("classnames", "^2.5.1"));
        var first = File.ReadAllText(LockFile.PathIn(_root));

        Manager().Add(_root, [new PackageRequest("classnames", "^2.5.1")]);

        File.ReadAllText(LockFile.PathIn(_root)).ShouldBe(first);
    }

    [Fact]
    public void RestoreFromLockFile_LockFileExists_InstallsExactlyWhatItPinsWithoutResolving()
    {
        Install(new PackageRequest("debug", "^4.3.0"));
        var pinned = JsonDocument.Parse(File.ReadAllText(LockFile.PathIn(_root)))
            .RootElement.GetProperty("packages").GetProperty("node_modules/ms")
            .GetProperty("version").GetString();

        Directory.Delete(Path.Combine(_root, "node_modules"), recursive: true);

        Manager().RestoreFromLockFile(_root).Succeeded.ShouldBeTrue();

        var installed = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, "node_modules", "ms", "package.json")));
        installed.RootElement.GetProperty("version").GetString().ShouldBe(pinned);
    }

    [Fact]
    public void Write_LockFileIsGivenToRealNpm_IsAcceptedByNpmCi()
    {
        // The bar for this whole exercise: npm agrees with what we wrote. If npm ci accepts the
        // lock file and installs from it, the format is right in the only way that matters.
        Install(new PackageRequest("classnames", "^2.5.1"), new PackageRequest("ms", "^2.1.3"));
        Directory.Delete(Path.Combine(_root, "node_modules"), recursive: true);

        var npm = NpmPackageManager.Find();
        if (npm is null)
        {
            return;
        }

        var (exitCode, output) = Fixtures.RealNpm.Run(_root, TimeSpan.FromMinutes(2), "ci");

        exitCode.ShouldBe(0, $"npm ci rejected the lock file:\n{output}");
        File.Exists(Path.Combine(_root, "node_modules", "classnames", "index.js")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "node_modules", "ms", "index.js")).ShouldBeTrue();
    }
}
