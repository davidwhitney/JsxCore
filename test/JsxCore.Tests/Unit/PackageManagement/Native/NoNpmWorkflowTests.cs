using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

[Trait("Category", "Network")]
public class NoNpmWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-nonpm-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Restore_NpmIsNotAvailable_CreatesAManifestAndInstallsWithoutIt()
    {
        // The whole point of the strategy: an empty directory, no npm, and a working node_modules
        // at the end of it. npm is reported as unavailable by pointing it at a path that is not one.
        Directory.CreateDirectory(_root);

        var selector = new PackageManagerSelector(
        [
            new NpmPackageManager("/definitely/not/npm"),
            new NativePackageManager()
        ]);

        var manager = selector.Select().ShouldNotBeNull();
        manager.Name.ShouldBe("native");

        var result = new DependencyRestorer(manager).Restore(
            _root, [new PackageRequest("classnames", "^2.5.1")]);

        result.Succeeded.ShouldBeTrue(result.Failure);
        File.Exists(Path.Combine(_root, "package.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "node_modules", "classnames", "index.js")).ShouldBeTrue();

        // And the result is something JsxCore can actually render a view from.
        new NodeModuleResolver(NodeModulesLayout.For(_root)).Resolve("classnames").ShouldNotBeNull();
    }

    [Fact]
    public void Install_PlatformSpecificOptionalDependencies_TakesOnlyTheOneForThisMachine()
    {
        // TypeScript ships one optional dependency per platform. Taking them all would be a large
        // download; taking none would leave no compiler.
        Directory.CreateDirectory(_root);
        var manager = new NativePackageManager();
        manager.CreateManifest(_root);

        var result = manager.Add(_root, [new PackageRequest("typescript", "^7.0.0", Development: true)]);

        result.Succeeded.ShouldBeTrue(result.Failure);

        var installed = Directory.GetDirectories(Path.Combine(_root, "node_modules", "@typescript"))
            .Select(Path.GetFileName)
            .ToList();

        installed.Count.ShouldBe(1);
        installed.Single().ShouldNotBeNull().ShouldContain(RegistryPackage.PlatformName());
        installed.Single().ShouldNotBeNull().ShouldContain(RegistryPackage.ArchitectureName());
    }
}
