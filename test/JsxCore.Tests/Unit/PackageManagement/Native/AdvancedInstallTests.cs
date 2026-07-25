using System.Text.Json;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

[Trait("Category", "Network")]
public class AdvancedInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-adv-" + Guid.NewGuid().ToString("n")[..8]);

    private NativePackageManager Manager() => new(report: _ => { });

    private void Manifest(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"), json);
    }

    private JsonElement LockPackages() =>
        JsonDocument.Parse(File.ReadAllText(LockFile.PathIn(_root))).RootElement.GetProperty("packages");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Install_OverrideForcesATransitiveVersion_ThatVersionIsInstalled()
    {
        // debug depends on ms ^2.1.3. The override pins something else, which is the whole point:
        // moving a dependency you do not declare yourself.
        Manifest("""{"name":"t","version":"1.0.0","dependencies":{"debug":"^4.3.0"},"overrides":{"ms":"2.1.2"}}""");

        Manager().InstallDeclared(_root).Succeeded.ShouldBeTrue();

        var installed = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, "node_modules", "ms", "package.json")));
        installed.RootElement.GetProperty("version").GetString().ShouldBe("2.1.2");
    }

    [Fact]
    public void Install_WorkspacesDeclared_LinksThemAndHoistsTheirDependencies()
    {
        Manifest("""{"name":"root","version":"1.0.0","workspaces":["packages/*"]}""");

        var a = Path.Combine(_root, "packages", "a");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "package.json"),
            """{"name":"@app/a","version":"1.0.0","dependencies":{"ms":"^2.1.3"}}""");

        Manager().InstallDeclared(_root).Succeeded.ShouldBeTrue();

        // The workspace is a link into the repository, not a copy of it.
        var link = Path.Combine(_root, "node_modules", "@app", "a");
        Directory.Exists(link).ShouldBeTrue();
        new DirectoryInfo(link).LinkTarget.ShouldNotBeNull();

        // Its dependency is hoisted to the top rather than installed inside it.
        Directory.Exists(Path.Combine(_root, "node_modules", "ms")).ShouldBeTrue();
    }

    [Fact]
    public void Install_WorkspacesDeclared_LockFileDescribesBothTheLinkAndTheTarget()
    {
        Manifest("""{"name":"root","version":"1.0.0","workspaces":["packages/*"]}""");
        var a = Path.Combine(_root, "packages", "a");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "package.json"), """{"name":"@app/a","version":"1.0.0"}""");

        Manager().InstallDeclared(_root);

        var packages = LockPackages();
        packages.GetProperty("node_modules/@app/a").GetProperty("link").GetBoolean().ShouldBeTrue();
        packages.GetProperty("node_modules/@app/a").GetProperty("resolved").GetString().ShouldBe("packages/a");
        packages.GetProperty("packages/a").GetProperty("name").GetString().ShouldBe("@app/a");
    }

    [Fact]
    public void Install_DependencyIsAGitUrl_IsFetchedFromTheHostArchive()
    {
        // A small, stable repository that is also a published package, so the shape is realistic.
        Manifest("""{"name":"t","version":"1.0.0","dependencies":{"ms":"github:vercel/ms#2.1.3"}}""");

        var result = Manager().InstallDeclared(_root);

        result.Succeeded.ShouldBeTrue(result.Failure);
        File.Exists(Path.Combine(_root, "node_modules", "ms", "package.json")).ShouldBeTrue();
    }

    [Fact]
    public void Install_DependencyIsAGitUrl_LockFileRecordsTheRepositoryNotATarball()
    {
        Manifest("""{"name":"t","version":"1.0.0","dependencies":{"ms":"github:vercel/ms#2.1.3"}}""");

        Manager().InstallDeclared(_root);

        LockPackages().GetProperty("node_modules/ms").GetProperty("resolved").GetString()
            .ShouldBe("git+https://github.com/vercel/ms.git#2.1.3");
    }

    [Fact]
    public void Install_GitDependencyIsInstalled_JsxCoreCanResolveItLikeAnyOther()
    {
        Manifest("""{"name":"t","version":"1.0.0","dependencies":{"ms":"github:vercel/ms#2.1.3"}}""");
        Manager().InstallDeclared(_root);

        new NodeModuleResolver(NodeModulesLayout.For(_root)).Resolve("ms").ShouldNotBeNull();
    }
}
