using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component;

/// <summary>
/// Runs the real sample application through <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
/// <remarks>
/// This is the compatibility check that matters most for a view engine: the factory relocates the
/// content root to the sample project's directory, and JsxCore resolves views, the working
/// directory and the toolchain relative to that. If content root handling were wrong, every one of
/// these would fail.
/// </remarks>
public class SampleAppTests : IClassFixture<SampleAppFactory>
{
    private readonly SampleAppFactory _factory;

    public SampleAppTests(SampleAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SampleApp_Starts_ResolvesItsContentRootAndCompilesItsViews()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
    }

    [Fact]
    public async Task SampleApp_ClientRenderedPage_CarriesTheModelAndMountScript()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        html.ShouldContain("""<div id="jsxcore-root"></div>""");
        html.ShouldContain("Hello from ASP.NET Core");
        // From the framework-neutral name, and the import map has to carry it or the browser
        // cannot resolve what the script it was just handed imports.
        html.ShouldContain("""import { mountView } from "@jsxcore/client";""");
        html.ShouldContain("<script type=\"importmap\">");
        html.ShouldContain("\"@jsxcore/client\":");
    }

    [Fact]
    public async Task SampleApp_ServerRenderedPage_ContainsFinishedMarkup()
    {
        var html = await _factory.CreateClient().GetStringAsync("/server");

        html.ShouldContain("<h1>Rendered on the server</h1>");
        html.ShouldContain("<td>Grace Hopper</td>");
        html.ShouldNotContain("mountView");
    }

    [Fact]
    public async Task SampleApp_ServerRenderedPage_CanCallRegisteredDotnetServices()
    {
        var html = await _factory.CreateClient().GetStringAsync("/dashboard");

        // Values produced by InventoryService during rendering.
        html.ShouldContain("Total stock value is");
        html.ShouldContain("Mechanical keyboard");
        html.ShouldContain("SKU-1001");
    }

    [Fact]
    public async Task SampleApp_MvcController_ResolvesTsxViewsThroughTheViewEngine()
    {
        var response = await _factory.CreateClient().GetAsync("/mvc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("jsxcore-root");
    }

    [Fact]
    public async Task SampleApp_HybridPage_IsRenderedOnTheServerAndMountedOnTheClient()
    {
        var html = await _factory.CreateClient().GetStringAsync("/hybrid");

        html.ShouldContain("Server rendered, then interactive");
        html.ShouldContain("running on the server");
        html.ShouldContain("mountView");
    }

    [Fact]
    public async Task SampleApp_CompiledModule_IsServedOverHttp()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");

        var start = html.IndexOf("/_jsx/v", StringComparison.Ordinal);
        var url = html[start..html.IndexOf('"', start)];

        var response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
    }
}

/// <summary>
/// Hosts the sample application in memory. Hot reload and file watching are switched off so the
/// test run does not spawn watchers or recompile behind the tests.
/// </summary>
public sealed class SampleAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
    }
}
