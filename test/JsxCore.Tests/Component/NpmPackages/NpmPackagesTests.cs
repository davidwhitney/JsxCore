using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using Jint.Runtime.Modules;
using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Modules;

namespace JsxCore.Tests.Component.NpmPackages;

/// <summary>
/// Server-rendered views importing real packages out of node_modules. The packages here are
/// deliberately varied: pure ESM, an exports map with conditions, a subpath, and CommonJS.
/// </summary>
public class NpmPackageTests
{
    private static NodeModuleResolver Resolver() => new(JsxProjectFixture.RepositoryRoot());

    private static async Task<string> RenderAsync(JsxProjectFixture project, string view, object? model = null)
    {
        await project.CompileAsync();
        var renderer = new JsxServerRenderer(
            project.Options, project.Compilation, project.RuntimeLayout, Resolver());

        var result = await renderer.RenderAsync(
            project.Locate(view), model, new Dictionary<string, object?>(),
            new ServiceCollection().BuildServiceProvider());
        return result.Html;
    }






    [Fact]
    public async Task Render_ViewImportsAnEsmPackage_RendersOnTheServer()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import { marked } from "marked";
            export default function Index({ model }: { model: { md: string } }) {
                return <div dangerouslySetInnerHTML={{ __html: marked.parse(model.md) as string }} />;
            }
            """);

        var html = await RenderAsync(project, "Home/Index", new { md = "# Hello" });

        html.ShouldContain("<h1>Hello</h1>");
    }

    [Fact]
    public async Task Render_ViewImportsACommonJsPackage_RendersOnTheServer()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import classNames from "classnames";
            export default function Index() {
                return <p class={classNames("a", { b: true, c: false })}>x</p>;
            }
            """);

        var html = await RenderAsync(project, "Home/Index");

        html.ShouldBe("""<p class="a b">x</p>""");
    }

    [Fact]
    public async Task Render_PackageHasDeepDependencies_ResolvesTheWholeGraph()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import { format } from "date-fns";
            export default function Index({ model }: { model: { when: string } }) {
                return <time>{format(new Date(model.when), "yyyy-MM-dd")}</time>;
            }
            """);

        var html = await RenderAsync(project, "Home/Index", new { when = "2026-07-29T00:00:00Z" });

        html.ShouldContain("2026-07-2");
    }

    [Fact]
    public async Task Render_PackageIsNotInstalled_ReportsWhereItLooked()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import x from "definitely-not-installed";
            export default function Index() { return <p>{String(x)}</p>; }
            """);

        var exception = await Should.ThrowAsync<JsxRenderException>(() => RenderAsync(project, "Home/Index"));

        exception.InnerException.ShouldBeOfType<JsxCoreException>()
            .Message.ShouldContain("not found in node_modules");
    }

    [Fact]
    public async Task Render_NodeModulesAreSwitchedOff_RejectsBareImports()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.AllowNodeModules = false;
        project.AddView("Home/Index.tsx", """
            import { marked } from "marked";
            export default function Index() { return <p>{typeof marked}</p>; }
            """);

        await project.CompileAsync();
        var renderer = new JsxServerRenderer(project.Options, project.Compilation, project.RuntimeLayout, null);

        var exception = await Should.ThrowAsync<JsxRenderException>(() => renderer.RenderAsync(
            project.Locate("Home/Index"), null, new Dictionary<string, object?>(),
            new ServiceCollection().BuildServiceProvider()));

        exception.InnerException!.Message.ShouldContain("switched off");
    }

    /// <summary>Writes a throwaway package into the project's own node_modules.</summary>
    private static void InstallFixturePackage(JsxProjectFixture project, string name, params (string File, string Body)[] files)
    {
        var directory = Path.Combine(project.Root, "node_modules", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "package.json"),
            $$"""{"name":"{{name}}","version":"1.0.0","type":"module","main":"index.js"}""");

        foreach (var (file, body) in files)
        {
            File.WriteAllText(Path.Combine(directory, file), body);
        }
    }

    private static async Task<string> RenderWithLocalPackagesAsync(JsxProjectFixture project, string view)
    {
        await project.CompileAsync();
        var renderer = new JsxServerRenderer(
            project.Options, project.Compilation, project.RuntimeLayout,
            new NodeModuleResolver(NodeModulesLayout.For(project.Root)));

        var result = await renderer.RenderAsync(
            project.Locate(view), null, new Dictionary<string, object?>(),
            new ServiceCollection().BuildServiceProvider());
        return result.Html;
    }

    [Fact]
    public async Task Render_PackageImportsJson_RendersOnTheServer()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        InstallFixturePackage(project, "json-pkg",
            ("index.js", """import data from "./data.json"; export const title = data.title;"""),
            ("data.json", """{"title":"from json"}"""));

        project.AddView("Home/Index.tsx", """
            import { title } from "json-pkg";
            export default function Index() { return <p>{title}</p>; }
            """);

        (await RenderWithLocalPackagesAsync(project, "Home/Index")).ShouldBe("<p>from json</p>");
    }

    [Theory]
    // Server rendering uses the engine's JSON module type rather than re-expressing the file as
    // "export default <json>". The two are not equivalent: the rewrite is JavaScript, so it accepts
    // these and Node would not. Loading them here would mean a view that renders on the server and
    // fails in the browser, so they have to be rejected on both sides.
    [InlineData("""{"title":"x" /* comment */}""")]
    [InlineData("{'title':'x'}")]
    [InlineData("{title:\"x\"}")]
    public async Task Render_PackageJsonWouldFailInABrowser_IsRejectedOnTheServerToo(string json)
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        InstallFixturePackage(project, "bad-json",
            ("index.js", """import data from "./data.json"; export const title = data.title;"""),
            ("data.json", json));

        project.AddView("Home/Index.tsx", """
            import { title } from "bad-json";
            export default function Index() { return <p>{title}</p>; }
            """);

        await Should.ThrowAsync<JsxRenderException>(() => RenderWithLocalPackagesAsync(project, "Home/Index"));
    }

    // -------------------------------------------------------------------------------------------
    // Client side: the same imports have to resolve in the browser, or a view that server-renders
    // would fail the moment it hydrated.
    // -------------------------------------------------------------------------------------------

    private static JsxProjectFixture PackageProject(string view)
    {
        var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        project.AddView("Home/Index.tsx", view);
        return project;
    }

    [Fact]
    public async Task ImportMap_ViewImportsAPackage_GainsAnEntryForIt()
    {
        using var project = PackageProject("""
            import { marked } from "marked";
            export default function Index() { return <p>{typeof marked}</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("\"marked\":");
        html.ShouldContain("/npm/0/marked/");
    }

    [Fact]
    public async Task AssetRequest_UrlFromTheImportMap_IsServedAsJavaScript()
    {
        using var project = PackageProject("""
            import { marked } from "marked";
            export default function Index() { return <p>{typeof marked}</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var url = ImportMapEntry(await host.GetStringAsync("/client/Index"), "marked");
        var response = await host.Client.GetAsync(url);

        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        (await response.Content.ReadAsStringAsync()).ShouldContain("export");
    }

    [Fact]
    public async Task AssetRequest_CommonJsPackage_IsServedWrappedAsAModule()
    {
        using var project = PackageProject("""
            import classNames from "classnames";
            export default function Index() { return <p class={classNames("a")}>x</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var url = ImportMapEntry(await host.GetStringAsync("/client/Index"), "classnames");
        var source = await host.GetStringAsync(url);

        source.ShouldContain("Wrapped from CommonJS");
        source.ShouldContain("export default module.exports;");
    }

    [Fact]
    public async Task AssetRequest_EveryFileInThePackageGraph_IsReachable()
    {
        // date-fns splits its work across many relative imports, so the whole graph must be there.
        using var project = PackageProject("""
            import { format } from "date-fns";
            export default function Index() { return <time>{format(new Date(0), "yyyy")}</time>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var entry = ImportMapEntry(await host.GetStringAsync("/client/Index"), "date-fns");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([entry]);
        while (pending.Count > 0)
        {
            var url = pending.Dequeue();
            if (!visited.Add(url))
            {
                continue;
            }

            var response = await host.Client.GetAsync(url);
            response.IsSuccessStatusCode.ShouldBeTrue($"'{url}' was referenced but is not served");

            foreach (Match match in Regex.Matches(
                         await response.Content.ReadAsStringAsync(), @"from\s*""(?<url>/_jsx/[^""]+)"""))
            {
                pending.Enqueue(match.Groups["url"].Value);
            }
        }

        visited.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task AssetRequest_PackageNoViewImports_IsNotServed()
    {
        using var project = PackageProject("""
            export default function Index() { return <p>no packages here</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var response = await host.Client.GetAsync("/_jsx/v" + project.Compilation.BuildId + "/npm/0/marked/package.json");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImportMap_NodeModulesAreSwitchedOff_ContainsNoPackages()
    {
        using var project = PackageProject("""
            import { marked } from "marked";
            export default function Index() { return <p>{typeof marked}</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project, options => options.AllowNodeModules = false);

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldNotContain("/npm/");
    }

    [Fact]
    public async Task ImportMap_EntryIsConfiguredExplicitly_OverridesTheGeneratedOne()
    {
        using var project = PackageProject("""
            import { marked } from "marked";
            export default function Index() { return <p>{typeof marked}</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(
            project, options => options.ImportMap["marked"] = "https://esm.sh/marked");

        var html = await host.GetStringAsync("/client/Index");

        ImportMapEntry(html, "marked").ShouldBe("https://esm.sh/marked");
    }

    [Fact]
    public async Task Render_HybridMode_UsesTheSamePackageOnBothSides()
    {
        using var project = PackageProject("""
            import classNames from "classnames";
            export default function Index() {
                return <p class={classNames("a", { b: true })}>hybrid</p>;
            }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/hybrid/Index");

        html.ShouldContain("""<p class="a b">hybrid</p>""");
        html.ShouldContain("\"classnames\":");
    }



    [Fact]
    public async Task ImportMap_PackageIsADevDependency_IsNotExportedToTheBrowser()
    {
        // typescript is a devDependency: it is never published, so a client-rendered view importing
        // it would work in development and fail in production. It gets no import map entry.
        using var project = PackageProject("""
            import * as ts from "typescript";
            export default function Index() { return <p>{typeof ts}</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");

        var map = Regex.Match(html, """<script type="importmap">(?<json>.*?)</script>""", RegexOptions.Singleline);
        using var document = JsonDocument.Parse(map.Groups["json"].Value);
        document.RootElement.GetProperty("imports").TryGetProperty("typescript", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Render_PackageIsADevDependency_StillResolvesOnTheServer()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import classNames from "classnames";
            export default function Index() { return <p>{typeof classNames}</p>; }
            """);

        var html = await RenderAsync(project, "Home/Index");

        html.ShouldBe("<p>function</p>");
    }


    [Fact]
    public async Task ImportMap_ViewProseResemblesAnImport_DoesNotWarnAboutAPackage()
    {
        // The scan reads specifiers out of compiled JavaScript, and this used to match the text.
        using var project = PackageProject("""
            export default function Index() {
                return <p>the server reads it from <code>node_modules</code> at run time</p>;
            }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldNotContain("_jsx(\":");
    }

    [Fact]
    public async Task ImportMap_RuntimeOwnsASpecifier_IsNotOverriddenByAPackage()
    {
        // Preact is staged and served by the runtime. Resolving it here as well would point the
        // browser at a second copy, which is a different module object from the one that rendered.
        using var project = PackageProject("""
            import { useState } from "preact/hooks";
            import { marked } from "marked";
            export default function Index() {
                const [n] = useState(1);
                return <p>{n}{typeof marked}</p>;
            }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/client/Index");

        ImportMapEntry(html, "preact/hooks").ShouldNotContain("/npm/");
        ImportMapEntry(html, "marked").ShouldContain("/npm/");
    }

    [Theory]
    // A 200 is not the same as a working module, so the served form is fetched and evaluated.
    [InlineData("classnames", """
        import classNames from "PACKAGE";
        globalThis.result = classNames("a", { b: true, c: false });
        """, "a b")]
    [InlineData("marked", """
        import { marked } from "PACKAGE";
        globalThis.result = marked.parse("# hi").trim();
        """, "<h1>hi</h1>")]
    public async Task ServedPackage_FetchedOverHttp_Evaluates(string specifier, string script, string expected)
    {
        using var project = PackageProject($$"""
            import "{{specifier}}";
            export default function Index() { return <p>x</p>; }
            """);
        await using var host = await JsxTestHost.StartAsync(project);

        var entry = ImportMapEntry(await host.GetStringAsync("/client/Index"), specifier);

        // Keyed by URL because the loader resolves every specifier to one, registered modules included.
        const string page = "http://localhost/__page.js";

        var engine = new Engine(options => options.EnableModules(new HttpModuleLoader(host.Client)));
        engine.Modules.Add(page, builder => builder.AddSource(script.Replace("PACKAGE", entry)));
        engine.Modules.Import(page);

        engine.Evaluate("globalThis.result").ToString().ShouldBe(expected);
    }

    /// <summary>Loads modules over HTTP, the way a browser would, from absolute asset URLs.</summary>
    private sealed class HttpModuleLoader(HttpClient client) : IModuleLoader
    {
        public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
        {
            var url = new Uri(
                referencingModuleLocation is null ? new Uri("http://localhost/") : new Uri(referencingModuleLocation),
                moduleRequest.Specifier);

            return new ResolvedSpecifier(moduleRequest, url.ToString(), url, SpecifierType.RelativeOrAbsolute);
        }

        public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
        {
            var response = client.GetAsync(resolved.Key).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            return ModuleFactory.BuildSourceTextModule(
                engine, resolved, response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                new ModuleParsingOptions());
        }
    }

    private static string ImportMapEntry(string html, string specifier)
    {
        var map = Regex.Match(html, """<script type="importmap">(?<json>.*?)</script>""", RegexOptions.Singleline);
        map.Success.ShouldBeTrue("the document has no import map");

        using var document = JsonDocument.Parse(map.Groups["json"].Value);
        return document.RootElement.GetProperty("imports").GetProperty(specifier).GetString()!;
    }
}
