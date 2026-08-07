using System.Diagnostics;
using JsxCore.Rendering;
using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// A render that will not end on its own has to be ended for it, either by the configured budget
/// running out or by the request it belongs to going away.
/// </summary>
public class RenderTimeoutTests
{
    /// <summary>A component that never returns, so only a constraint can stop it.</summary>
    private const string Forever = """
        export default function Forever() {
            let n = 0;
            while (true) { n = n + 1; }
            return <p>{n}</p>;
        }
        """;

    [Fact]
    public async Task Render_ViewNeverReturns_IsEndedByTheConfiguredTimeout()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.ServerRendering.Timeout = TimeSpan.FromMilliseconds(200);
        project.AddView("Home/Forever.tsx", Forever);
        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();
        var elapsed = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => renderer.RenderAsync(
            project.Locate("Home/Forever"), null, new Dictionary<string, object?>(), services));

        elapsed.Stop();

        // The message identifies which budget ran out: the render's own, rather than one of the
        // per-entry ones a single render used to be allowed several of.
        exception.ShouldBeOfType<JsxRenderException>()
            .InnerException.ShouldBeOfType<TimeoutException>()
            .Message.ShouldContain("server rendering timeout");

        // Deliberately loose. The point is that the render ended rather than ran on, not how
        // promptly a loaded machine noticed.
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Render_RequestAbortedWhileTheViewRuns_StopsRunningJavaScript()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        // Far enough out that only the abort can plausibly be what ends this render.
        project.Options.ServerRendering.Timeout = TimeSpan.FromSeconds(30);
        project.AddView("Home/Forever.tsx", Forever);
        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();

        // Cancelled after the render is under way, so the engine has to notice mid-flight. A token
        // that was already cancelled would be caught while waiting for a pooled engine instead, and
        // would prove nothing about the JavaScript.
        using var aborted = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var elapsed = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => renderer.RenderAsync(
            project.Locate("Home/Forever"), null, new Dictionary<string, object?>(), services, aborted.Token));

        elapsed.Stop();

        exception.ShouldNotBeNull();
        exception.ShouldBeAssignableTo<OperationCanceledException>();
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    /// <summary>A component that returns immediately, so nothing but a broken budget can stop it.</summary>
    private const string Fast = """
        export default function Fast() { return <p>ok</p>; }
        """;

    /// <summary>
    /// Turning the budget off has to mean no budget rather than one that has already run out. Each
    /// of these asks for that in the way a host plausibly would.
    /// </summary>
    public static TheoryData<TimeSpan> DisabledTimeouts => new()
    {
        TimeSpan.Zero,
        Timeout.InfiniteTimeSpan,
        TimeSpan.MaxValue
    };

    [Theory]
    [MemberData(nameof(DisabledTimeouts))]
    public async Task Render_TimeoutDisabled_RendersRatherThanExpiringImmediately(TimeSpan timeout)
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.ServerRendering.Timeout = timeout;
        project.AddView("Home/Fast.tsx", Fast);
        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();

        var result = await renderer.RenderAsync(
            project.Locate("Home/Fast"), null, new Dictionary<string, object?>(), services);

        result.Html.ShouldBe("<p>ok</p>");
    }

    [Theory]
    [MemberData(nameof(DisabledTimeouts))]
    public async Task Render_TimeoutDisabled_StillHonoursAnAbortedRequest(TimeSpan timeout)
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;

        // With no budget to end it, the abort token is the only thing left that can.
        project.Options.ServerRendering.Timeout = timeout;
        project.AddView("Home/Forever.tsx", Forever);
        await project.CompileAsync();

        var renderer = project.CreateServerRenderer();
        var services = new ServiceCollection().BuildServiceProvider();

        using var aborted = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var elapsed = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => renderer.RenderAsync(
            project.Locate("Home/Forever"), null, new Dictionary<string, object?>(), services, aborted.Token));

        elapsed.Stop();

        exception.ShouldBeAssignableTo<OperationCanceledException>();
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }
}
