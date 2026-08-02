using System.Net;
using Microsoft.AspNetCore.Builder;
using System.Text.RegularExpressions;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// Importing a static asset from a view: <c>dotnet:wwwroot/images/logo.svg</c> resolves, type
/// checks, and hands back the URL ASP.NET Core already serves the file from.
/// </summary>
/// <remarks>
/// Compiled with the real toolchain and rendered with the real engine, because the whole feature
/// rests on what tsc does with a specifier it has no opinion about.
/// </remarks>
public class StaticAssetTests
{
    private const string Logo = """<svg xmlns="http://www.w3.org/2000/svg"><rect width="8" height="8"/></svg>""";

    private static JsxProjectFixture ProjectWithLogo()
    {
        var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", Logo);
        project.AddView("Home/Index.tsx", """
            import logo from "dotnet:wwwroot/images/logo.svg";
            export default function Index() {
                return <img src={logo} alt="logo" />;
            }
            """);
        return project;
    }

    [Fact]
    public async Task Import_OfAnImage_RendersTheUrlItIsServedFrom()
    {
        using var project = ProjectWithLogo();
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index");

        // The file's own URL, not a copy under JsxCore's prefix: the application serves wwwroot
        // and JsxCore has no business duplicating it.
        result.Html.ShouldContain("src=\"/images/logo.svg\"");
        result.Html.ShouldNotContain("/_jsx/");
    }

    [Fact]
    public async Task Import_OfAnImage_IsRewrittenToAModuleTheBrowserCanLoad()
    {
        using var project = ProjectWithLogo();
        await project.CompileAsync();

        var compiled = await File.ReadAllTextAsync(
            Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));

        // The scheme never reaches a browser, which has nothing to resolve it with.
        compiled.ShouldNotContain("dotnet:wwwroot");
        compiled.ShouldContain("_static/images/logo.svg.js");

        var module = Path.Combine(project.Layout.OutputDirectory, "_static", "images", "logo.svg.js");
        (await File.ReadAllTextAsync(module)).ShouldContain("\"/images/logo.svg\"");
    }

    [Fact]
    public async Task Import_OfAnImage_IsTypedAsTheUrlString()
    {
        // The ambient declarations are what stop this being an unresolved module in an editor, and
        // TypeChecking.Error is what makes a failure here a failing test rather than a warning.
        using var project = ProjectWithLogo();
        project.Options.TypeChecking = TypeCheckingMode.Warn;
        project.AddView("Home/Typed.tsx", """
            import logo from "dotnet:wwwroot/images/logo.svg";
            const upper: string = logo.toUpperCase();
            export default function Typed() { return <p>{upper}</p>; }
            """);

        var build = await project.CompileAsync();

        build.Result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_ThroughTheHostedPipeline_ServesTheFileFromWwwroot()
    {
        using var project = ProjectWithLogo();
        await using var host = await JsxTestHost.StartAsync(project, configureApp: app => app.UseStaticFiles());

        var html = await host.GetStringAsync("/server/Index");
        html.ShouldContain("src=\"/images/logo.svg\"");

        var response = await host.Client.GetAsync("/images/logo.svg");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        (await response.Content.ReadAsStringAsync()).ShouldBe(Logo);
    }

    [Fact]
    public async Task Import_OfAStylesheet_PutsALinkInTheDocument()
    {
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/css/app.css", "body { color: rebeccapurple; }");
        project.AddView("Home/Index.tsx", """
            import "dotnet:wwwroot/css/app.css";
            export default function Index() { return <p>styled</p>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        // A stylesheet import binds nothing, so the only place it can mean anything is the document.
        html.ShouldContain("""<link rel="stylesheet" href="/css/app.css">""");
    }

    [Fact]
    public async Task Import_OfAStylesheet_EmitsDependenciesBeforeTheViewThatUsesThem()
    {
        // Order comes from the import graph rather than from what rendered first, because CSS is
        // order dependent and two views must not produce two different cascades.
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/card.css", ".card { color: red; }");
        project.AddFile("wwwroot/page.css", ".page { color: blue; }");
        project.AddView("Shared/Card.tsx", """
            import "dotnet:wwwroot/card.css";
            export function Card() { return <div class="card" />; }
            """);
        project.AddView("Home/Index.tsx", """
            import "dotnet:wwwroot/page.css";
            import { Card } from "../Shared/Card.tsx";
            export default function Index() { return <Card />; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/server/Index");

        var hrefs = Regex.Matches(html, @"<link rel=""stylesheet"" href=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        hrefs.ShouldBe(["/card.css", "/page.css"]);
    }

    [Fact]
    public async Task Import_OfAStylesheet_IsLinkedOnEveryRenderMode()
    {
        // A client-rendered view never runs on the server, but its stylesheet still has to be in
        // the document: nothing else will ever put it there.
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/app.css", "body { margin: 0; }");
        project.AddView("Home/Index.tsx", """
            import "dotnet:wwwroot/app.css";
            export default function Index() { return <p>styled</p>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);

        foreach (var route in new[] { "/client/Index", "/server/Index", "/hybrid/Index" })
        {
            (await host.GetStringAsync(route))
                .ShouldContain("""<link rel="stylesheet" href="/app.css">""", customMessage: route);
        }
    }

    [Fact]
    public async Task Import_OfAFileThatIsNotThere_IsLeftAloneAndReported()
    {
        // Guessing a URL would produce a page that 404s an image in a browser and nowhere else.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            import logo from "dotnet:wwwroot/images/missing.svg";
            export default function Index() { return <img src={logo} />; }
            """);

        await project.CompileAsync();

        var linked = JsxCore.Compilation.Assets.ViewAssetLinker.Link(project.Layout);

        linked.Unresolved.ShouldContain("dotnet:wwwroot/images/missing.svg");
        (await File.ReadAllTextAsync(Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js")))
            .ShouldContain("dotnet:wwwroot/images/missing.svg");
    }

    [Fact]
    public async Task Import_ThatClimbsOutOfTheWebRoot_IsRefused()
    {
        using var project = JsxProjectFixture.Create();
        project.AddFile("secrets.txt", "not yours");
        project.AddFile("wwwroot/ok.txt", "fine");
        project.AddView("Home/Index.tsx", """
            import secret from "dotnet:wwwroot/../secrets.txt";
            export default function Index() { return <p>{secret}</p>; }
            """);

        await project.CompileAsync();
        var linked = JsxCore.Compilation.Assets.ViewAssetLinker.Link(project.Layout);

        linked.Unresolved.ShouldContain("dotnet:wwwroot/../secrets.txt");
        Directory.Exists(Path.Combine(project.Layout.OutputDirectory, "_static")).ShouldBeFalse();
    }

    [Fact]
    public async Task Import_RemovedFromAView_RemovesTheModuleWrittenForIt()
    {
        using var project = ProjectWithLogo();
        await project.CompileAsync();

        var module = Path.Combine(project.Layout.OutputDirectory, "_static", "images", "logo.svg.js");
        File.Exists(module).ShouldBeTrue();

        project.AddView("Home/Index.tsx", "export default function Index() { return <p>plain</p>; }");
        await project.Compilation.CompileAsync();

        File.Exists(module).ShouldBeFalse();
    }
}
