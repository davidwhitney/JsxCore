using System.Text.Json;
using System.Text.RegularExpressions;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// Stylesheets a package imports from its own JavaScript, as design systems built with
/// <c>vite-plugin-lib-inject-css</c> do: <c>import "../assets/Button.css"</c> beside the component
/// code, usually minified to <c>import"../assets/Button.css"</c>.
/// </summary>
/// <remarks>
/// Neither the server engine nor the browser can evaluate CSS as a module, so the import becomes an
/// empty module and the document links the stylesheet instead, as it does for a view's own CSS.
/// </remarks>
public class PackageStylesheetTests
{
    private const string Plain = """
        import "./base.css";
        import { inner } from "./inner.js";
        export function label() { return inner(); }
        """;

    private const string Minified = """import"./base.css";import{inner}from"./inner.js";export function label(){return inner()}""";

    private static JsxProjectFixture PackageProject(string entry, bool minifiedInner = false)
    {
        JsxProjectFixture.EnsureRepositoryPackages();

        var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        // Declared, or it would be withheld from the browser as a devDependency.
        project.AddFile("package.json", """{ "dependencies": { "fake-design": "1.0.0" } }""");
        project.AddFile("node_modules/fake-design/package.json",
            """{ "name": "fake-design", "type": "module", "main": "index.js" }""");
        project.AddFile("node_modules/fake-design/index.js", entry);
        project.AddFile("node_modules/fake-design/inner.js", minifiedInner
            ? """import"./inner.css";export function inner(){const greeting="widget";return greeting}"""
            : """
              import "./inner.css";
              export function inner() { const greeting = "widget"; return greeting; }
              """);
        project.AddFile("node_modules/fake-design/base.css", ".base { border: 1px solid; }");
        project.AddFile("node_modules/fake-design/inner.css", ".inner { color: rebeccapurple; }");
        project.AddView("Home/page.css", ".page { margin: 0; }");
        project.AddView("Home/Index.tsx", """
            import "./page.css";
            import { label } from "fake-design";
            export default function Index() { return <p class="widget">{label()}</p>; }
            """);
        project.AddView("Home/Other.tsx", """
            export default function Other() { return <p>plain</p>; }
            """);
        return project;
    }

    private static IReadOnlyList<string> LinksIn(string html) =>
        Regex.Matches(html, @"<link rel=""stylesheet"" href=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

    private static string ImportMapEntry(string html, string specifier)
    {
        var map = Regex.Match(html, """<script type="importmap">(?<json>.*?)</script>""", RegexOptions.Singleline);
        map.Success.ShouldBeTrue("the document has no import map");

        using var document = JsonDocument.Parse(map.Groups["json"].Value);
        return document.RootElement.GetProperty("imports").GetProperty(specifier).GetString()!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Render_PackageImportsItsOwnStylesheet_RendersOnTheServer(bool minified)
    {
        using var project = PackageProject(minified ? Minified : Plain, minified);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/server/Index");

        html.ShouldContain("""<p class="widget">widget</p>""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_PackageImportsStylesheets_LinksThemBeforeTheViewsOwn(bool minified)
    {
        using var project = PackageProject(minified ? Minified : Plain, minified);
        await using var host = await JsxTestHost.StartAsync(project);

        var links = LinksIn(await host.GetStringAsync("/server/Index"));

        links.Count.ShouldBe(3);
        links[0].ShouldEndWith("/fake-design/base.css");
        links[1].ShouldEndWith("/fake-design/inner.css");
        links[2].ShouldContain("Home/page.css");
    }

    [Fact]
    public async Task AssetRequest_PackageStylesheetLink_IsServedAsCss()
    {
        using var project = PackageProject(Minified, minifiedInner: true);
        await using var host = await JsxTestHost.StartAsync(project);

        var href = LinksIn(await host.GetStringAsync("/client/Index"))[1];
        var response = await host.Client.GetAsync(href);

        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
        (await response.Content.ReadAsStringAsync()).ShouldContain("rebeccapurple");
    }

    [Fact]
    public async Task AssetRequest_PackageJavaScript_ImportsItsStylesheetAsAnEmptyModule()
    {
        using var project = PackageProject(Minified, minifiedInner: true);
        await using var host = await JsxTestHost.StartAsync(project);

        var entry = await host.GetStringAsync(ImportMapEntry(await host.GetStringAsync("/client/Index"), "fake-design"));
        var module = Regex.Match(entry, @"[""'](?<url>[^""']+/base\.css\.js)[""']");

        module.Success.ShouldBeTrue($"the stylesheet import should point at its module, not the CSS: {entry}");
        var response = await host.Client.GetAsync(module.Groups["url"].Value);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        (await response.Content.ReadAsStringAsync()).Trim().ShouldBe("export {};");
    }

    [Fact]
    public async Task Document_ViewDoesNotImportThePackage_DoesNotLinkItsStylesheets()
    {
        using var project = PackageProject(Plain);
        await using var host = await JsxTestHost.StartAsync(project);

        LinksIn(await host.GetStringAsync("/server/Other")).ShouldBeEmpty();
    }

    [Fact]
    public async Task AssetRequest_MinificationIsOn_MinifiesTheJavaScriptAndLeavesTheCss()
    {
        if (EsbuildToolchainLocator.Locate(JsxProjectFixture.RepositoryRoot()) is not { } esbuild)
        {
            return;
        }

        using var project = PackageProject(Plain);
        await using var host = await JsxTestHost.StartAsync(project, options =>
        {
            options.Minify = true;
            options.MinifierPath = esbuild.ExecutablePath;
        });

        var html = await host.GetStringAsync("/client/Index");
        var entry = await host.GetStringAsync(ImportMapEntry(html, "fake-design"));
        var inner = Regex.Match(entry, @"[""'](?<url>[^""']+/inner\.js)[""']").Groups["url"].Value;

        // One stylesheet in the batch must not make esbuild reject every module, which would ship
        // the whole graph unminified without a word.
        (await host.GetStringAsync(inner)).ShouldNotContain("greeting");
        (await host.GetStringAsync(LinksIn(html)[1])).ShouldContain("rebeccapurple");
    }
}
