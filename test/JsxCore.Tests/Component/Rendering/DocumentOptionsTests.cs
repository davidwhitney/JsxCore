using System.Net;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Rendering;

public class DocumentOptionsTests
{
    [Fact]
    public async Task DocumentSettings_OverriddenOnAResult_ApplyToThatResponse()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, configureApp: app =>
            app.MapGet("/custom", () => Results.Extensions.Jsx("Home/Index", new { name = "X", items = Array.Empty<string>() }, result =>
            {
                result.Title = "Overridden title";
                result.Language = "fr";
                result.ContainerId = "app";
                result.HeadContent = "<meta name=\"custom\" content=\"yes\">";
                result.BodyContent = "<footer id=\"extra\">extra</footer>";
                result.BodyAttributes["data-theme"] = "dark";
                result.StatusCode = 201;
            })));

        var response = await host.Client.GetAsync("/custom");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The explicit title wins over the view's own head export.
        html.ShouldContain("<title>Overridden title</title>");
        html.ShouldNotContain("Index page");
        html.ShouldContain("""<html lang="fr">""");
        html.ShouldContain("""<div id="app">""");
        html.ShouldContain("""<meta name="custom" content="yes">""");
        html.ShouldContain("""<footer id="extra">extra</footer>""");
        html.ShouldContain("data-theme=\u0022dark\u0022");
    }

    [Fact]
    public async Task DocumentSettings_OverriddenOnOneResult_DoNotLeakIntoTheNext()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, configureApp: app =>
            app.MapGet("/custom", () => Results.Extensions.Jsx("Home/Index", new { name = "X", items = Array.Empty<string>() }, result =>
            {
                result.Language = "de";
                result.ContainerId = "app";
            })));

        (await host.GetStringAsync("/custom")).ShouldContain("""<html lang="de">""");

        var untouched = await host.GetStringAsync("/client/Index");
        untouched.ShouldContain("""<html lang="en">""");
        untouched.ShouldContain("""<div id="jsxcore-root">""");
    }

    /// <remarks>
    /// A result overriding any document setting renders from a copy of the configured options, and
    /// the copy used to be made without the nonce callback. Setting a title was therefore enough to
    /// serve a page with no nonce on any of its scripts, which under a policy naming one renders
    /// nothing at all. The unit tests never saw it: they hand the writer a nonce directly.
    /// </remarks>
    [Fact]
    public async Task Nonce_WithDocumentSettingsOverriddenOnAResult_IsStillOnEveryScript()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(
            project,
            configure: options => options.Document.Nonce = _ => "r4nd0m",
            configureApp: app =>
                app.MapGet("/titled", () => Results.Extensions.Jsx("Home/Index", new { name = "X", items = Array.Empty<string>() }, result =>
                    result.Title = "Overridden title")));

        var html = await host.GetStringAsync("/titled");

        html.ShouldContain("<title>Overridden title</title>");

        var tags = Regex.Matches(html, "<script[^>]*>").Select(match => match.Value).ToList();
        tags.Count.ShouldBeGreaterThan(2);
        tags.ShouldAllBe(tag => tag.Contains("nonce=\"r4nd0m\""));
    }

    [Fact]
    public async Task DocumentTemplate_SuppliedOnAResult_ReplacesTheWholeDocument()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, configureApp: app =>
            app.MapGet("/bare", () => Results.Extensions.Jsx("Home/Index", new { name = "X", items = Array.Empty<string>() }, result =>
                result.DocumentTemplate = context => $"<!-- {context.ViewName} --><p>{context.RenderMode}</p>")));

        var html = await host.GetStringAsync("/bare");

        html.ShouldBe("<!-- Home/Index --><p>Client</p>");
    }
}
