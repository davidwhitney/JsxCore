using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// DefinitelyTyped tarballs are rooted at the package's short name rather than "package/", which is
// the case that put @types/react into node_modules/@types/react/react.
[Trait("Category", "Network")]
public class TypesTarballTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-types-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Extract_TarballRootedAtThePackageName_StripsItLikeNpmDoes()
    {
        // DefinitelyTyped tarballs are rooted at "react/" rather than "package/". npm strips
        // whatever the first directory is called, and so must this.
        using var http = new HttpClient();
        await using var tarball = await http.GetStreamAsync(
            "https://registry.npmjs.org/@types/react/-/react-19.2.18.tgz");

        var destination = Path.Combine(_root, "extracted");
        await PackageArchive.ExtractAsync(tarball, destination, integrity: null);

        File.Exists(Path.Combine(destination, "index.d.ts"))
            .ShouldBeTrue("index.d.ts should be at the root, not under a second 'react' directory");
        Directory.Exists(Path.Combine(destination, "react")).ShouldBeFalse();
    }

    [Fact]
    public void Add_ScopedTypesPackage_LandsDirectlyInItsOwnDirectory()
    {
        Directory.CreateDirectory(_root);
        new NativePackageManager(report: _ => { }).CreateManifest(_root);

        var result = new NativePackageManager(report: _ => { })
            .Add(_root, [
                new JsxCore.Compilation.Provisioning.PackageManagement.PackageRequest("react"),
                new JsxCore.Compilation.Provisioning.PackageManagement.PackageRequest("react-dom"),
                new JsxCore.Compilation.Provisioning.PackageManagement.PackageRequest("@types/react", "", Development: true),
                new JsxCore.Compilation.Provisioning.PackageManagement.PackageRequest("@types/react-dom", "", Development: true)
            ]);

        result.Succeeded.ShouldBeTrue(result.Failure);

        var package = Path.Combine(_root, "node_modules", "@types", "react");
        File.Exists(Path.Combine(package, "index.d.ts"))
            .ShouldBeTrue($"index.d.ts should sit directly in {package}, not under another directory");
    }
}
