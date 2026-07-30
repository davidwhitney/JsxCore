using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Runtimes;

/// <summary>
/// React as a framework: compiled against, server rendered, and served to the browser.
/// </summary>
/// <remarks>
/// React is the harder of the two to support and every part of that shows up here. It publishes
/// CommonJS, so the browser gets it through the same wrapper that serves any other package; its
/// entry points are branched re-exports, so their named exports have to be followed; and its server
/// renderer builds a MessageChannel and a TextEncoder while its own module body runs. None of that
/// is visible in a view, which is the point.
/// </remarks>
[Trait("Category", "Network")]
public class ReactModeTests
{
    private static JsxProjectFixture ReactProject()
    {
        var project = JsxProjectFixture.Create(JsFramework.React);
        project.Options.TypeChecking = TypeCheckingMode.Off;
        return project;
    }

    [Fact]
    public async Task Compile_View_TargetsReactsJsxRuntimeRatherThanPreacts()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>hello</p>; }
            """);

        await project.CompileAsync();

        var emitted = await File.ReadAllTextAsync(
            Path.Combine(project.Compilation.Layout.OutputDirectory, "Home", "Index.js"));

        emitted.ShouldContain("react/jsx-runtime");
        emitted.ShouldNotContain("preact");
    }

    [Fact]
    public async Task Render_SimpleComponent_ProducesHtml()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <h1>Hello {model.name}</h1>;
            }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index", new { name = "React" });

        // React separates adjacent text with comment markers so it can hydrate them later, which is
        // also the clearest evidence this is React's renderer and not another one.
        result.Html.ShouldContain("<h1>Hello ");
        result.Html.ShouldContain("React");
    }

    [Fact]
    public async Task Render_ViewImportsFromReact_ResolvesThroughTheCommonJsWrapper()
    {
        // react/jsx-runtime is a branched re-export: without following it, jsxs is not exported and
        // the view cannot be linked at all.
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            import { createElement } from "react";

            export default function Index() {
                return createElement("p", null, "made by react");
            }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldContain("made by react");
    }

    [Fact]
    public async Task Hooks_UsedDuringServerRender_FallBackToTheirInitialValues()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            import { useState } from "react";

            export default function Index() {
                const [count] = useState(7);
                return <p>{count}</p>;
            }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldContain("7");
    }

    [Fact]
    public async Task Render_ComponentTree_ResolvesImportedComponents()
    {
        using var project = ReactProject();
        project.AddView("Shared/Badge.tsx", """
            export function Badge({ text }: { text: string }) { return <span>{text}</span>; }
            """);
        project.AddView("Home/Index.tsx", """
            import { Badge } from "../Shared/Badge.tsx";

            export default function Index() {
                return <div><Badge text="nested" /></div>;
            }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldContain("nested");
        result.Html.ShouldContain("<span");
    }

    [Fact]
    public async Task Render_ModelValueContainsMarkup_EscapesIt()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { text: string } }) {
                return <p>{model.text}</p>;
            }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index", new { text = "<script>alert(1)</script>" });

        result.Html.ShouldNotContain("<script>alert(1)</script>");
        result.Html.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public async Task HeadExport_ViewDeclaresOne_IsExposedToTheDocument()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export const head = { title: "From React" };
            export default function Index() { return <p>body</p>; }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Head.ShouldNotBeNull();
        result.Head!.Title.ShouldBe("From React");
    }

    [Fact]
    public async Task Render_AsyncComponent_IsRejectedWithAnExplanatoryMessage()
    {
        using var project = ReactProject();
        project.AddView("Home/Async.tsx", """
            export default async function Async() { return <p>never</p>; }
            """);

        await project.CompileAsync();

        var exception = await Should.ThrowAsync<JsxRenderException>(() => project.RenderAsync("Home/Async"));

        exception.Message.ShouldContain("synchronous");
    }

    [Fact]
    public async Task ImportMap_ReactIsUsed_PointsAtTheWrappedPackagesRatherThanAtPreact()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>hi</p>; }
            """);

        await project.CompileAsync();

        await using var host = await JsxTestHost.StartAsync(project, options =>
            options.TypeDefinitions.ApplicationAssembly = FrameworkAssembly.For(JsFramework.React));

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("@jsxcore/react/client");
        html.ShouldContain("/npm/");
        html.ShouldNotContain("preact");
    }

    [Fact]
    public async Task Assets_ReactIsUsed_ServesTheClientEntryAndTheWrappedPackages()
    {
        using var project = ReactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>hi</p>; }
            """);

        await project.CompileAsync();

        await using var host = await JsxTestHost.StartAsync(project, options =>
            options.TypeDefinitions.ApplicationAssembly = FrameworkAssembly.For(JsFramework.React));

        var html = await host.GetStringAsync("/client/Index");

        var entry = ExtractUrl(html, "/react/client.js");
        var react = ExtractUrl(html, "/npm/0/react/index.js");

        (await host.Client.GetAsync(entry)).IsSuccessStatusCode.ShouldBeTrue($"{entry} should be served");

        var served = await host.GetStringAsync(react);

        // Wrapped from CommonJS, so it arrives as a module with real named exports rather than the
        // require-based source npm publishes.
        served.ShouldContain("export default module.exports;");
        served.ShouldContain("export const createElement =");
    }

    private static string ExtractUrl(string html, string ending)
    {
        var quoted = html.Split('"').FirstOrDefault(part => part.EndsWith(ending, StringComparison.Ordinal));
        return quoted ?? throw new InvalidOperationException($"No URL ending in '{ending}' in:{Environment.NewLine}{html}");
    }
}
