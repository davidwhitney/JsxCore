using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.HotReload;

// A hot update re-renders in place rather than reloading, which needs the mounted root to expose an
// update function. Building the element in the reload client instead produced a blank page.
public class HotUpdateTests
{
    private static JsxProjectFixture Project()
    {
        var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());

        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>original</p>; }
            """);
        return project;
    }

    [Fact]
    public async Task MountView_Always_ExposesAnUpdateFunctionForHotReload()
    {
        using var project = Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");
        var entry = System.Text.RegularExpressions.Regex.Match(html, @"""(/_jsx/[^""]*client[^""]*)""");

        var clientModule = await host.GetStringAsync(entry.Success
            ? entry.Groups[1].Value
            : "/_jsx/v" + project.Compilation.BuildId + "/runtime/client.js");

        // The contract the reload client depends on: the runtime supplies the update, because only
        // it knows how to build an element it can render.
        clientModule.ShouldContain("update:");
    }

    [Fact]
    public async Task HotReloadClient_RuntimeSuppliesNoUpdate_FallsBackToAFullReload()
    {
        using var project = Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var client = await host.GetStringAsync(
            "/_jsx/v" + project.Compilation.BuildId + "/runtime/hmr-client.js");

        client.ShouldContain("typeof root.update !== \"function\"");
        client.ShouldContain("location.reload()");
    }

    [Fact]
    public async Task HotReloadClient_AppliesAnUpdate_DoesNotBuildTheElementItself()
    {
        // The bug this covers: the client built an element literal itself and handed it to the
        // mounted root, which Preact renders as nothing.
        using var project = Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var client = await host.GetStringAsync(
            "/_jsx/v" + project.Compilation.BuildId + "/runtime/hmr-client.js");

        client.ShouldNotContain("jsxcore.element");
        client.ShouldContain("root.update(Component)");
    }
}
