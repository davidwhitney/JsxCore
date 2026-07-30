using JsxCore.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Rendering;

public class ServerRenderingTests
{
    [Fact]
    public async Task Render_SimpleComponent_ProducesHtml()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <h1 className="title">Hello {model.name}</h1>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", new { name = "World" });

        result.Html.ShouldBe("""<h1 class="title">Hello World</h1>""");
    }

    [Fact]
    public async Task Render_ModelValueContainsMarkup_EscapesIt()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <p>{model.name}</p>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", new { name = "<script>alert(1)</script>" });

        result.Html.ShouldBe("<p>&lt;script>alert(1)&lt;/script></p>");
    }

    [Fact]
    public async Task Render_ViewImportsAComponent_ResolvesItThroughTheModuleGraph()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", """
            import type { JsxNode } from "@jsxcore/runtime";
            export function Card({ title, children }: { title: string; children?: JsxNode }) {
                return <section><h2>{title}</h2>{children}</section>;
            }
            """);
        project.AddView("Home/Index.tsx", """
            import { Card } from "../Shared/Card.tsx";
            export default function Index({ model }: { model: { items: string[] } }) {
                return (
                    <Card title="Items">
                        <ul>{model.items.map((item) => <li key={item}>{item}</li>)}</ul>
                    </Card>
                );
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", new { items = new[] { "a", "b" } });

        result.Html.ShouldBe("<section><h2>Items</h2><ul><li>a</li><li>b</li></ul></section>");
    }

    [Fact]
    public async Task HeadExport_ViewDeclaresOne_IsExposedToTheDocument()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export const head = (model: { name: string }) => ({
                title: `Hello ${model.name}`,
                meta: [{ name: "description", content: "test" }]
            });
            export default function Index() { return <p>body</p>; }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", new { name = "World" });

        result.Head.ShouldNotBeNull();
        result.Head!.Title.ShouldBe("Hello World");
        result.Head.Meta.ShouldNotBeNull();
        result.Head.Meta![0]["content"].ShouldBe("test");
    }

    [Fact]
    public async Task Render_VoidElementsAndBooleanAttributes_ProducesValidHtml()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index() {
                return <p><img src="/a.png" alt="" /><input type="checkbox" checked disabled={false} /><br /></p>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index");

        // alt="" is deliberately preserved: it marks an image as decorative.
        result.Html.ShouldBe("""<p><img src="/a.png" alt/><input type="checkbox" checked/><br/></p>""");
    }

    [Fact]
    public async Task Render_StyleObjectAndEventHandler_ConvertsStyleAndOmitsTheHandler()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index() {
                return <div style={{ marginTop: 8, color: "red" }} onClick={() => {}}>x</div>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("""<div style="margin-top:8px;color:red;">x</div>""");
    }

    [Fact]
    public async Task DotnetGlobal_CalledFromAView_IsInvokedSynchronously()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.Globals.Register("Greeter", new Greeter());
        project.AddView("Home/Index.tsx", """
            import { dotnet } from "@jsxcore/runtime";
            export default function Index({ model }: { model: { name: string } }) {
                const greeter = dotnet.Greeter as { Greet(name: string): string };
                return <p>{greeter.Greet(model.name)}</p>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index", new { name = "David" });

        result.Html.ShouldBe("<p>Hello, David!</p>");
    }

    [Fact]
    public async Task DotnetGlobal_MemberAccessedInCamelCase_ResolvesToTheClrMember()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.Globals.Register("Greeter", new Greeter());
        project.AddView("Home/Index.tsx", """
            import { dotnet } from "@jsxcore/runtime";
            export default function Index() {
                const greeter = dotnet.Greeter as { greet(name: string): string };
                return <p>{greeter.greet("world")}</p>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("<p>Hello, world!</p>");
    }

    [Fact]
    public async Task DotnetGlobal_RegisteredAsScoped_IsResolvedFromTheRequestScope()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.Globals.Register<Greeter>("Greeter");

        var services = new ServiceCollection()
            .AddScoped(_ => new Greeter { Salutation = "Hi" })
            .BuildServiceProvider();

        project.AddView("Home/Index.tsx", """
            import { dotnet } from "@jsxcore/runtime";
            export default function Index() {
                const greeter = dotnet.Greeter as { greet(name: string): string };
                return <p>{greeter.greet("scoped")}</p>;
            }
            """);
        await project.CompileAsync();

        using var scope = services.CreateScope();
        var result = await project.RenderAsync("Home/Index", services: scope.ServiceProvider);

        result.Html.ShouldBe("<p>Hi, scoped!</p>");
    }

    [Fact]
    public async Task Render_PackageIsNotInstalled_ReportsAHelpfulError()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            import something from "some-npm-package";
            export default function Index() { return <p>{String(something)}</p>; }
            """);
        await project.CompileAsync();

        var exception = await Should.ThrowAsync<JsxRenderException>(() => project.RenderAsync("Home/Index"));

        exception.InnerException.ShouldBeOfType<JsxCoreException>()
            .Message.ShouldContain("node_modules");
    }

    [Fact]
    public async Task Render_JavaScriptThrows_SurfacesTheErrorWithAStackTrace()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Boom.tsx", """
            export default function Boom() {
                throw new Error("component exploded");
            }
            """);
        await project.CompileAsync();

        var exception = await Should.ThrowAsync<JsxRenderException>(() => project.RenderAsync("Home/Boom"));

        exception.Message.ShouldContain("component exploded");
    }

    [Fact]
    public async Task Render_AsyncComponent_IsRejectedWithAnExplanatoryMessage()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Async.tsx", """
            export default async function Async() {
                return <p>never</p>;
            }
            """);
        await project.CompileAsync();

        var exception = await Should.ThrowAsync<JsxRenderException>(() => project.RenderAsync("Home/Async"));

        exception.Message.ShouldContain("synchronous");
    }

    [Fact]
    public async Task Hooks_UsedDuringServerRender_FallBackToTheirInitialValues()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            import { useState } from "preact/hooks";
            export default function Index() {
                const [count] = useState(7);
                return <output>{count}</output>;
            }
            """);
        await project.CompileAsync();

        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("<output>7</output>");
    }

    [Fact]
    public async Task Render_CalledRepeatedly_ReusesPooledEnginesCorrectly()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { n: number } }) {
                return <p>{model.n}</p>;
            }
            """);
        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var view = project.Locate("Home/Index");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new Dictionary<string, object?>();

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(async n =>
            (await renderer.RenderAsync(view, new { n }, context, services)).Html));

        results.ShouldBe(Enumerable.Range(0, 25).Select(n => $"<p>{n}</p>"), ignoreOrder: true);
    }

    private sealed class Greeter
    {
        public string Salutation { get; init; } = "Hello";
        public string Greet(string name) => $"{Salutation}, {name}!";
    }

    public sealed class PerRequestService
    {
        public string Id { get; init; } = "";
        public string get() => Id;
    }

    [Fact]
    public async Task DotnetGlobal_HeldAtModuleScope_StillReachesTheCurrentRequest()
    {
        // Modules are evaluated once per engine and engines are reused between requests, so a view
        // capturing a global outside its component used to hold the first request's instance for
        // the life of the engine. Silent, and only wrong for per-request objects.
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        var requests = 0;
        project.Options.Globals.Register("Scoped", _ => new PerRequestService { Id = "request-" + (++requests) });

        project.AddView("Home/Index.tsx", """
            import { dotnet } from "@jsxcore/runtime";
            const captured = (dotnet as any).Scoped;
            export default function Index() { return <p>{captured.get()}</p>; }
            """);

        await project.CompileAsync();
        var renderer = new JsxServerRenderer(project.Options, project.Compilation, project.RuntimeLayout);
        var services = new ServiceCollection().BuildServiceProvider();
        var view = project.Locate("Home/Index");

        var first = await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);
        var second = await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);

        first.Html.ShouldBe("<p>request-1</p>");
        second.Html.ShouldBe("<p>request-2</p>");
    }

    [Fact]
    public async Task DotnetGlobal_ReadInsideAComponent_IsResolvedPerRequest()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        var requests = 0;
        project.Options.Globals.Register("Scoped", _ => new PerRequestService { Id = "request-" + (++requests) });

        project.AddView("Home/Index.tsx", """
            import { dotnet } from "@jsxcore/runtime";
            export default function Index() { return <p>{(dotnet as any).Scoped.get()}</p>; }
            """);

        await project.CompileAsync();
        var renderer = new JsxServerRenderer(project.Options, project.Compilation, project.RuntimeLayout);
        var services = new ServiceCollection().BuildServiceProvider();
        var view = project.Locate("Home/Index");

        await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);
        var second = await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);

        second.Html.ShouldBe("<p>request-2</p>");
    }
}
