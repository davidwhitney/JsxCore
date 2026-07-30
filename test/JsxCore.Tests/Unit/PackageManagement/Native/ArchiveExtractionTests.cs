using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

[Trait("Category", "Network")]
public class ArchiveExtractionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-mode-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Extract_PackageContainsAnExecutable_KeepsItExecutable()
    {
        // The TypeScript compiler is a binary inside a package. Losing the executable bit on the
        // way out of the archive means it installs and then cannot be run, which is reported as a
        // missing compiler rather than as a broken one.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        new NativePackageManager(report: _ => { })
            .Add(_root, [new PackageRequest("typescript", "^7.0.0", Development: true)])
            .Succeeded.ShouldBeTrue();

        var compilers = Directory.EnumerateFiles(_root, "tsc", SearchOption.AllDirectories).ToList();
        compilers.ShouldNotBeEmpty();

        foreach (var compiler in compilers)
        {
            File.GetUnixFileMode(compiler).HasFlag(UnixFileMode.UserExecute)
                .ShouldBeTrue($"{compiler} should be executable");
        }
    }

    [Theory]
    // The conventional npm layout, and DefinitelyTyped's, which roots at the package's short name.
    [InlineData("package/index.js", "index.js")]
    [InlineData("package/lib/deep/thing.js", "lib/deep/thing.js")]
    [InlineData("react/index.d.ts", "index.d.ts")]
    // What .NET 8 hands back for a GNU-format archive: the ustar prefix field, which GNU fills with
    // access and change times, joined onto the front of the real name.
    // NUL padding, as .NET 8 actually hands it over, not spaces.
    [InlineData("\0\0\0\0 15232743476 15232743476/react/index.d.ts", "index.d.ts")]
    [InlineData("\0\0 1523 1523/package/lib/thing.js", "lib/thing.js")]
    public void RelativePathOf_AnyRootConvention_StripsExactlyTheRoot(string entryName, string expected) =>
        PackageArchive.RelativePathOf(entryName).ShouldBe(expected);

    [Theory]
    [InlineData("package")]
    [InlineData("react/")]
    [InlineData("")]
    public void RelativePathOf_NothingBelowTheRoot_IsNull(string entryName) =>
        PackageArchive.RelativePathOf(entryName).ShouldBeNull();
}
