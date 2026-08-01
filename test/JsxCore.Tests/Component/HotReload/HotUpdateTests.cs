using System.Text.RegularExpressions;
using System.Text;
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

        var entryUrl = entry.Success
            ? entry.Groups[1].Value
            : "/_jsx/v" + project.Compilation.BuildId + "/runtime/client.js";

        var clientModule = await host.GetStringAsync(entryUrl);

        // Follow what the entry imports: the update is supplied by the code shared between the
        // frameworks rather than by either one's own entry point. What matters is that it reaches
        // the browser, not which file it arrives in.
        var served = new StringBuilder(clientModule);
        var directory = entryUrl[..(entryUrl.LastIndexOf('/') + 1)];

        foreach (Match import in Regex.Matches(clientModule, @"from\s+""\./([^""]+)"""))
        {
            served.Append(await host.GetStringAsync(directory + import.Groups[1].Value));
        }

        // The contract the reload client depends on: the runtime supplies the update, because only
        // it knows how to build an element it can render.
        served.ToString().ShouldContain("update:");
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
