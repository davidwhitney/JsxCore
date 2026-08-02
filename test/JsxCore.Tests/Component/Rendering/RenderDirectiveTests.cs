using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// A view declaring where it runs, through the real compiler and the real pipeline.
/// </summary>
/// <remarks>
/// The whole feature rests on tsc emitting the directive prologue through unchanged, so nothing
/// here fakes the compiler.
/// </remarks>
public class RenderDirectiveTests
{
    private const string Markup = "<h1>Hello World</h1>";

    private static JsxProjectFixture Project(string index)
    {
        var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", index);
        return project;
    }

    private const string Body = """
        export default function Index({ model }: { model: { name: string } }) {
            return <h1>Hello {model.name}</h1>;
        }
        """;

    [Fact]
    public async Task UseServer_RendersOnTheServerWithNoScript()
    {
        using var project = Project($"\"use server\";\n{Body}");
        await using var host = await JsxTestHost.StartAsync(project);

        // The default route names no mode, so the directive decides.
        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain(Markup);
        html.ShouldNotContain("mountView");
    }

    [Fact]
    public async Task UseClient_RendersOnTheClient()
    {
        using var project = Project($"\"use client\";\n{Body}");
        project.Options.DefaultRenderMode = RenderMode.Server;

        await using var host = await JsxTestHost.StartAsync(
            project, options => options.DefaultRenderMode = RenderMode.Server);

        var html = await host.GetStringAsync("/client/Index");

        // The directive wins over a default that says otherwise.
        html.ShouldContain("mountView");
        html.ShouldNotContain(Markup);
    }

    [Fact]
    public async Task NoDirective_FallsBackToTheConfiguredDefault()
    {
        using var project = Project(Body);
        await using var host = await JsxTestHost.StartAsync(
            project, options => options.DefaultRenderMode = RenderMode.Server);

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain(Markup);
        html.ShouldNotContain("mountView");
    }

    [Fact]
    public async Task ExplicitMode_WinsOverTheDirective()
    {
        // The endpoint is deciding for one response and knows why.
        using var project = Project($"\"use client\";\n{Body}");
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/server/Index");

        html.ShouldContain(Markup);
        html.ShouldNotContain("mountView");
    }

    [Fact]
    public async Task UseServer_WithAnExplicitHybridMode_RendersAndHydrates()
    {
        using var project = Project($"\"use server\";\n{Body}");
        await using var host = await JsxTestHost.StartAsync(project);

        var html = await host.GetStringAsync("/hybrid/Index");

        html.ShouldContain(Markup);
        html.ShouldContain("\"hydrate\":true");
    }

    [Fact]
    public async Task Directive_OnAnImportedComponent_DoesNotDecideTheResponse()
    {
        // Otherwise a shared component would quietly change every page that imports it.
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", """
            "use server";
            export function Card({ name }: { name: string }) { return <h1>Hello {name}</h1>; }
            """);
        project.AddView("Home/Index.tsx", """
            import { Card } from "../Shared/Card.tsx";
            export default function Index({ model }: { model: { name: string } }) {
                return <Card name={model.name} />;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("mountView");
        html.ShouldNotContain(Markup);
    }

    [Fact]
    public async Task Directive_AfterALicenceHeader_IsStillRead()
    {
        using var project = Project($"// Copyright someone.\n\"use server\";\n{Body}");
        await using var host = await JsxTestHost.StartAsync(project);

        (await host.GetStringAsync("/client/Index")).ShouldContain(Markup);
    }

    [Fact]
    public async Task Directive_IsRecordedForAPrecompiledApplication()
    {
        // A published server has no sources, so the answer has to survive in the compiled output.
        using var project = Project($"\"use server\";\n{Body}");
        await project.CompileAsync();

        var manifest = JsxCore.Compilation.Assets.ViewManifest.ReadFrom(project.Layout.OutputDirectory);

        manifest.ModeFor("Home/Index.js").ShouldBe(RenderMode.Server);
    }

    [Fact]
    public async Task Directive_Removed_TakesEffectOnTheNextBuild()
    {
        using var project = Project($"\"use server\";\n{Body}");
        await project.CompileAsync();
        project.Compilation.Views.ModeFor("Home/Index.js").ShouldBe(RenderMode.Server);

        project.AddView("Home/Index.tsx", Body);
        await project.Compilation.CompileAsync();

        project.Compilation.Views.ModeFor("Home/Index.js").ShouldBeNull();
    }
}
