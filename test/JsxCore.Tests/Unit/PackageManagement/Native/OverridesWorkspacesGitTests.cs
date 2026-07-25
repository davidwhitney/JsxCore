using System.Text.Json;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

public class OverrideSetTests
{
    private static OverrideSet From(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")[..8] + ".json");
        File.WriteAllText(path, json);
        try { return OverrideSet.From(PackageManifest.Read(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RangeFor_OverrideIsFlat_AppliesEverywhere() =>
        From("""{"overrides":{"semver":"^7.5.2"}}""").RangeFor("semver", "anything").ShouldBe("^7.5.2");

    [Fact]
    public void RangeFor_OverrideIsScopedToAParent_AppliesOnlyInsideIt()
    {
        var overrides = From("""{"overrides":{"foo":{"bar":"2.0.0"}}}""");

        overrides.RangeFor("bar", "foo").ShouldBe("2.0.0");
        overrides.RangeFor("bar", "somethingElse").ShouldBeNull();
    }

    [Fact]
    public void RangeFor_ParentHasBothItsOwnVersionAndChildren_ReadsBoth()
    {
        var overrides = From("""{"overrides":{"foo":{".":"1.0.0","bar":"2.0.0"}}}""");

        overrides.RangeFor("foo", null).ShouldBe("1.0.0");
        overrides.RangeFor("bar", "foo").ShouldBe("2.0.0");
    }

    [Fact]
    public void RangeFor_ScopedRuleAndBlanketRuleBothApply_PrefersTheScopedOne() =>
        From("""{"overrides":{"bar":"1.0.0","foo":{"bar":"2.0.0"}}}""")
            .RangeFor("bar", "foo").ShouldBe("2.0.0");

    [Fact]
    public void From_ManifestHasNoOverrides_IsEmpty() =>
        From("""{"name":"t"}""").IsEmpty.ShouldBeTrue();
}

public class WorkspaceDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-ws-" + Guid.NewGuid().ToString("n")[..8]);

    private void Write(string relativePath, string json)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar), "package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Discover_WorkspacesAreGlobbed_FindsEachPackage()
    {
        Write("", """{"name":"root","workspaces":["packages/*"]}""");
        Write("packages/a", """{"name":"@app/a","version":"1.0.0"}""");
        Write("packages/b", """{"name":"@app/b","version":"2.0.0"}""");

        var found = Workspaces.Discover(_root, PackageManifest.In(_root));

        found.Select(w => w.Name).OrderBy(n => n).ShouldBe(["@app/a", "@app/b"]);
        found.Single(w => w.Name == "@app/b").Version.ShouldBe("2.0.0");
    }

    [Fact]
    public void Discover_WorkspacesAreAnObject_ReadsThePackagesArray()
    {
        Write("", """{"name":"root","workspaces":{"packages":["libs/*"]}}""");
        Write("libs/one", """{"name":"one","version":"1.0.0"}""");

        Workspaces.Discover(_root, PackageManifest.In(_root)).Single().Name.ShouldBe("one");
    }

    [Fact]
    public void Discover_ADirectoryHasNoManifest_IsNotAWorkspace()
    {
        Write("", """{"name":"root","workspaces":["packages/*"]}""");
        Directory.CreateDirectory(Path.Combine(_root, "packages", "empty"));

        Workspaces.Discover(_root, PackageManifest.In(_root)).ShouldBeEmpty();
    }

    [Fact]
    public void DependenciesOf_WorkspacesDependOnEachOther_OnlyExternalOnesNeedFetching()
    {
        Write("", """{"name":"root","workspaces":["packages/*"]}""");
        Write("packages/a", """{"name":"a","version":"1.0.0","dependencies":{"b":"1.0.0","ms":"^2.1.3"}}""");
        Write("packages/b", """{"name":"b","version":"1.0.0"}""");

        var requests = Workspaces.DependenciesOf(Workspaces.Discover(_root, PackageManifest.In(_root)));

        requests.Select(r => r.Name).ShouldBe(["ms"]);
    }

    [Fact]
    public void Discover_NoWorkspacesDeclared_FindsNone()
    {
        Write("", """{"name":"root"}""");

        Workspaces.Discover(_root, PackageManifest.In(_root)).ShouldBeEmpty();
    }
}
