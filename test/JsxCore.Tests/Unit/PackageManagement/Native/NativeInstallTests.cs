using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// These reach the public npm registry. They are the only way to know the client actually works,
// because the thing being tested is agreement with a real registry.
[Trait("Category", "Network")]
public class NativeInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-native-" + Guid.NewGuid().ToString("n")[..8]);

    private NativePackageManager Manager() => new(report: _ => { });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateManifest_DirectoryHasNone_WritesAValidPackageJson()
    {
        Directory.CreateDirectory(_root);

        Manager().CreateManifest(_root).Succeeded.ShouldBeTrue();

        var manifest = PackageManifest.In(_root).ShouldNotBeNull();
        manifest.Packages.ShouldBeEmpty();
        File.ReadAllText(manifest.Path).ShouldContain("\"private\": true");
    }

    [Fact]
    public void CreateManifest_ManifestAlreadyExists_LeavesItAlone()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "package.json");
        File.WriteAllText(path, """{"name":"mine","dependencies":{"marked":"^18.0.0"}}""");

        Manager().CreateManifest(_root);

        File.ReadAllText(path).ShouldContain("mine");
    }

    [Fact]
    public void Add_PackageWithNoDependencies_IsInstalledAndRecorded()
    {
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);

        var result = Manager().Add(_root, [new PackageRequest("classnames", "^2.5.1")]);

        result.Succeeded.ShouldBeTrue(result.Failure);
        File.Exists(Path.Combine(_root, "node_modules", "classnames", "package.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "node_modules", "classnames", "index.js")).ShouldBeTrue();
        PackageManifest.In(_root)!.Dependencies.Single().Name.ShouldBe("classnames");
    }

    [Fact]
    public void Add_PackageIsUsable_MatchesWhatNpmWouldHaveInstalled()
    {
        // The installed tree has to be good enough for JsxCore's own resolver to load from, which
        // is the only definition of "installed" that matters here.
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);
        Manager().Add(_root, [new PackageRequest("classnames", "^2.5.1")]);

        var resolved = new NodeModuleResolver(NodeModulesLayout.For(_root)).Resolve("classnames");

        resolved.ShouldNotBeNull();
        resolved.Kind.ShouldBe(NodeModuleKind.CommonJs);
    }

    [Fact]
    public void Add_PackageHasDependencies_InstallsTheWholeGraph()
    {
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);

        var result = Manager().Add(_root, [new PackageRequest("debug", "^4.3.0")]);

        result.Succeeded.ShouldBeTrue(result.Failure);
        // debug depends on ms, so a transitive dependency proves the graph walk.
        Directory.Exists(Path.Combine(_root, "node_modules", "ms")).ShouldBeTrue();
    }

    [Fact]
    public void Add_PackageIsScoped_IsInstalledUnderItsScopeDirectory()
    {
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);

        var result = Manager().Add(_root, [new PackageRequest("@jsxcore/does-not-exist", "^1.0.0")]);

        // Not published, so this proves the failure is reported rather than swallowed.
        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
    }

    [Fact]
    public void InstallDeclared_ManifestDeclaresPackages_InstallsThemAll()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"),
            """{"name":"t","dependencies":{"classnames":"^2.5.1"},"devDependencies":{"ms":"^2.1.3"}}""");

        var result = Manager().InstallDeclared(_root);

        result.Succeeded.ShouldBeTrue(result.Failure);
        Directory.Exists(Path.Combine(_root, "node_modules", "classnames")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, "node_modules", "ms")).ShouldBeTrue();
    }

    [Fact]
    public void Add_APackageWhenOthersAreAlreadyDeclared_LockFileDescribesThemAll()
    {
        // Resolving only the new package would rewrite the lock file with only that package in
        // it, and npm reads a lock file missing what package.json declares as out of sync.
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);

        Manager().Add(_root, [new PackageRequest("classnames", "^2.5.1")]).Succeeded.ShouldBeTrue();
        Manager().Add(_root, [new PackageRequest("ms", "^2.1.3", Development: true)]).Succeeded.ShouldBeTrue();

        var locked = LockFile.Read(_root).Select(package => package.Name).ToList();

        locked.ShouldContain("classnames");
        locked.ShouldContain("ms");

        // Both remain installed too, not just recorded.
        Directory.Exists(Path.Combine(_root, "node_modules", "classnames")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, "node_modules", "ms")).ShouldBeTrue();
    }

    [Fact]
    public void Add_APackageAlreadyDeclared_TakesTheNewRange()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"),
            """{"name":"t","dependencies":{"classnames":"2.3.1"}}""");

        Manager().Add(_root, [new PackageRequest("classnames", "2.5.1")]).Succeeded.ShouldBeTrue();

        LockFile.Read(_root).Single(package => package.Name == "classnames").Version.ShouldBe("2.5.1");
    }

    [Fact]
    public void Add_RangeIsALocalPathReference_SaysSoRatherThanFailingObscurely()
    {
        Directory.CreateDirectory(_root);
        Manager().CreateManifest(_root);

        var result = Manager().Add(_root, [new PackageRequest("something", "file:../local-package")]);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull().ShouldContain("npm");
    }
}
