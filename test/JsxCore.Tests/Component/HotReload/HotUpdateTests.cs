using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.HotReload;

// A hot update re-renders in place rather than reloading. The element it renders has to be one the
// active runtime understands, which is why this is tested against both of them: building the
// element in the reload client produced a blank page under Preact.
public class HotUpdateTests
{
    private static JsxProjectFixture Project(JsxRuntimeMode runtime)
    {
        var project = JsxProjectFixture.Create();
        project.Options.Runtime = runtime;
        project.Options.TypeChecking = TypeCheckingMode.Off;
        if (runtime == JsxRuntimeMode.Preact)
        {
            project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        }

        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>original</p>; }
            """);
        return project;
    }

    [Theory]
    [InlineData(JsxRuntimeMode.Builtin)]
    [InlineData(JsxRuntimeMode.Preact)]
    public async Task MountView_AnyRuntime_ExposesAnUpdateFunctionForHotReload(JsxRuntimeMode runtime)
    {
        using var project = Project(runtime);
        await using var host = await JsxTestHost.StartAsync(project, options => options.Runtime = runtime);

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
        using var project = Project(JsxRuntimeMode.Builtin);
        await using var host = await JsxTestHost.StartAsync(project);

        var client = await host.GetStringAsync(
            "/_jsx/v" + project.Compilation.BuildId + "/runtime/hmr-client.js");

        client.ShouldContain("typeof root.update !== \"function\"");
        client.ShouldContain("location.reload()");
    }

    [Fact]
    public async Task HotReloadClient_AppliesAnUpdate_DoesNotBuildTheElementItself()
    {
        // The bug this covers: the client built a built-in element literal and handed it to
        // whichever runtime was mounted, which Preact renders as nothing.
        using var project = Project(JsxRuntimeMode.Builtin);
        await using var host = await JsxTestHost.StartAsync(project);

        var client = await host.GetStringAsync(
            "/_jsx/v" + project.Compilation.BuildId + "/runtime/hmr-client.js");

        client.ShouldNotContain("jsxcore.element");
        client.ShouldContain("root.update(Component)");
    }
}
