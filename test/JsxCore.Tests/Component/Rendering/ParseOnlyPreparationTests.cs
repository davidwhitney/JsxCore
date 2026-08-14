using JsxCore.Rendering;
using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// An application whose views can be recompiled prepares its modules the cheaper way, which changes
/// when the engine works out what a module means but must never change what it renders.
/// </summary>
/// <remarks>
/// The parses are shared by every engine in the pool, and with the analysis pass declined each
/// engine fills in what it needs as it reaches a node — so several engines rendering at once are
/// writing that state onto one shared tree. The engine supports exactly this, and these renders are
/// how JsxCore says it depends on it.
/// </remarks>
public class ParseOnlyPreparationTests
{
    private const string Card = """
        import type { JsxNode } from "dotnet:rendering";
        export function Card({ title, children }: { title: string; children?: JsxNode }) {
            return <section class="card"><h2>{title}</h2>{children}</section>;
        }
        """;

    private const string View = """
        import { Card } from "../Shared/Card.tsx";
        import type { ViewProps } from "dotnet:rendering";
        export default function Index({ model }: ViewProps<{ name: string; items: string[] }>) {
            return (
                <Card title={`Hello ${model.name}`}>
                    <ul>{model.items.map((item) => <li key={item}>{item}</li>)}</ul>
                </Card>
            );
        }
        """;

    private sealed record Model(string Name, string[] Items);

    private static Model TheModel => new("world", ["one", "two", "three"]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Render_PreparedEitherWay_ProducesTheSameMarkup(bool watching)
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.WatchForChanges = watching;
        project.AddView("Shared/Card.tsx", Card);
        project.AddView("Home/Index.tsx", View);

        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", TheModel);

        result.Html.ShouldBe(
            "<section class=\"card\"><h2>Hello world</h2><ul><li>one</li><li>two</li><li>three</li></ul></section>");
    }

    [Fact]
    public async Task Render_WhileWatching_FromSeveralEnginesAtOnce_IsConsistent()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.WatchForChanges = true;
        project.Options.ServerRendering.MaxPooledEngines = 4;
        project.AddView("Shared/Card.tsx", Card);
        project.AddView("Home/Index.tsx", View);

        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();
        var view = project.Locate("Home/Index");

        // Task.Run, because a render completes synchronously while a pool slot is free: called in a
        // loop they would run one after another on one engine and share no parse at all.
        var renders = new Task<ServerRenderResult>[8];
        for (var i = 0; i < renders.Length; i++)
        {
            renders[i] = Task.Run(() =>
                renderer.RenderAsync(view, TheModel, new Dictionary<string, object?>(), services));
        }

        var results = await Task.WhenAll(renders);

        results.Select(result => result.Html).Distinct().Count().ShouldBe(1);
        results[0].Html.ShouldContain("<h2>Hello world</h2>");
    }
}
