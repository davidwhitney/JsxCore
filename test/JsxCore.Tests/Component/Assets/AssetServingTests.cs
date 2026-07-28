using System.Net;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Assets;

public class AssetServingTests
{
    [Fact]
    public async Task AssetRequest_ForModuleOrRuntime_IsServed()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");
        var moduleUrl = HostedViews.ModuleUrl().Match(html).Value;
        moduleUrl.ShouldNotBeEmpty();

        var module = await host.Client.GetAsync(moduleUrl);
        module.StatusCode.ShouldBe(HttpStatusCode.OK);
        module.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        (await module.Content.ReadAsStringAsync()).ShouldContain("../Shared/Card.js");

        // The runtime is served straight out of the assembly.
        var runtimeUrl = moduleUrl[..moduleUrl.IndexOf("/views", StringComparison.Ordinal)] + "/runtime/client.js";
        var runtime = await host.Client.GetAsync(runtimeUrl);
        runtime.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await runtime.Content.ReadAsStringAsync()).ShouldContain("export function createRoot");
    }

    [Fact]
    public async Task AssetRequest_ForAnImportedComponent_IsServedSoTheBrowserCanResolveIt()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");
        var prefix = HostedViews.BuildPrefix().Match(html).Value;

        var card = await host.Client.GetAsync($"{prefix}/views/Shared/Card.js");

        card.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await card.Content.ReadAsStringAsync()).ShouldContain("export function Card");
    }

    [Fact]
    public async Task AssetResponse_UrlCarriesTheBuildId_IsMarkedImmutable()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");
        var response = await host.Client.GetAsync(HostedViews.ModuleUrl().Match(html).Value);

        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromDays(365));
        response.Headers.CacheControl.Public.ShouldBeTrue();
    }

    [Fact]
    public async Task AssetRequest_PathEscapesTheOutputDirectory_IsRejected()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var response = await host.Client.GetAsync("/_jsx/v0/views/..%2F..%2F..%2Ftsconfig.json");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssetRequest_PathIsNotOurs_FallsThroughToTheRestOfThePipeline()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        var response = await host.Client.GetAsync("/_jsx/v0/views/Nope.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
