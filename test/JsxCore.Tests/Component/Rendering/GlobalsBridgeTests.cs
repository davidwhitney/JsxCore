using JsxCore.Rendering;
using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// What a registered .NET global costs a render: whatever the view actually reads, and nothing at
/// all for the views that read none.
/// </summary>
/// <remarks>
/// A registration is resolved from the request's service scope, so it can be a database context, a
/// localizer or anything else a scope builds on demand. Most views in an application read none of
/// them, and every view that renders while one is registered used to pay for all of them.
/// </remarks>
public class GlobalsBridgeTests
{
    public sealed class Clock
    {
        public string Now() => "tick";
    }

    [Fact]
    public async Task RegisteredGlobal_ImportedButNeverRead_IsNotResolved()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        var resolutions = 0;
        project.Options.Globals.Register("Clock", _ =>
        {
            resolutions++;
            return new Clock();
        });

        // Imports the global and holds it, which is what a view sharing a module with one that does
        // use it looks like. Nothing here reads a member, so nothing here needs the instance.
        project.AddView("Home/Index.tsx", """
            import { Clock } from "dotnet:globals";
            const held = Clock;
            export default function Index() { return <p>{typeof held}</p>; }
            """);

        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();
        var view = project.Locate("Home/Index");

        await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);
        var second = await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);

        second.Html.ShouldBe("<p>object</p>");
        resolutions.ShouldBe(0);
    }

    [Fact]
    public async Task RegisteredGlobal_ReadByTheView_IsResolvedOncePerRender()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        var resolutions = 0;
        project.Options.Globals.Register("Clock", _ =>
        {
            resolutions++;
            return new Clock();
        });

        // Read twice, so a bridge resolved per read rather than per render would be caught.
        project.AddView("Home/Index.tsx", """
            import { Clock } from "dotnet:globals";
            export default function Index() {
                return <p>{(Clock as any).Now()}{(Clock as any).Now()}</p>;
            }
            """);

        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();
        var view = project.Locate("Home/Index");

        await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);
        var second = await renderer.RenderAsync(view, null, new Dictionary<string, object?>(), services);

        second.Html.ShouldBe("<p>ticktick</p>");
        resolutions.ShouldBe(2);
    }
}
