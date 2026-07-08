using System.Net;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Rendering;

public class RenderModeTests
{
    [Fact]
    public async Task ClientMode_ViewIsRequested_ReturnsShellWithSerialisedModelAndNoMarkup()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("<title>Index page</title>");
        html.ShouldContain("""<div id="jsxcore-root"></div>""");
        html.ShouldContain("""id="jsxcore-model""");
        html.ShouldContain("\"name\":\"World\"");
        html.ShouldContain("import { mountView }");

        // The component itself must not have run.
        html.ShouldNotContain("<h2>Hello World</h2>");
    }

    [Fact]
    public async Task ServerMode_ViewIsRequested_ReturnsFinishedMarkupWithNoMountScript()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, o => o.HotReload = false);

        var html = await host.GetStringAsync("/server/Index");

        html.ShouldContain("<h2>Hello World</h2>");
        html.ShouldContain("<li>alpha</li>");
        html.ShouldNotContain("mountView");
        html.ShouldNotContain("jsxcore-model");
    }

    [Fact]
    public async Task HybridMode_ViewIsRequested_ReturnsBothMarkupAndMountScript()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/hybrid/Index");

        html.ShouldContain("<h2>Hello World</h2>");
        html.ShouldContain("mountView");
        html.ShouldContain("""id="jsxcore-model""");
    }

    [Fact]
    public async Task HeadExport_AnyRenderMode_PopulatesTheDocumentHead()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        // Client mode never runs the component, but the head export must still apply.
        (await host.GetStringAsync("/client/Index")).ShouldContain("<title>Index page</title>");
        (await host.GetStringAsync("/server/Index")).ShouldContain("<title>Index page</title>");
        (await host.GetStringAsync("/hybrid/Index")).ShouldContain("<title>Index page</title>");
    }
}
