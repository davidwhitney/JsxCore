using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// <c>isServerRender()</c> answers which pass is running.
/// </summary>
/// <remarks>
/// It used to read the globals bridge, which the host leaves undefined when an application has
/// registered nothing, so an application with no globals was told it was on the client while it was
/// rendering on the server. The guard the documentation recommends was wrong in exactly the
/// applications least likely to notice.
/// </remarks>
public class IsServerRenderTests
{
    private const string View = """
        "use server";
        import { isServerRender } from "dotnet:rendering";

        export default function Index() {
            return <p>{isServerRender() ? "server" : "client"}</p>;
        }
        """;

    [Fact]
    public async Task IsServerRender_WithNoGlobalsRegistered_IsStillTrue()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", View);

        project.Options.Globals.Registrations.ShouldBeEmpty();

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("<p>server</p>");
    }

    [Fact]
    public async Task IsServerRender_WithGlobalsRegistered_IsTrue()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.Globals.Register("Clock", new object());
        project.AddView("Home/Index.tsx", View);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("<p>server</p>");
    }

    [Fact]
    public async Task IsServerRender_OnAClientRenderedPage_IsFalseInTheBrowser()
    {
        // Nothing sets the flag in a browser, so the same view reports the other side there. The
        // markup never reaches the server in this mode, which is what this asserts.
        //
        // The directive is dropped without naming the newline after it. The line endings in a raw
        // string literal are the ones in this file as checked out, so matching on "\n" quietly
        // matched nothing wherever git had written CRLF, and the view kept the "use server" this
        // was removing.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", View.Replace("\"use server\";", string.Empty));

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        html.ShouldNotContain("<p>server</p>");
        html.ShouldContain("mountView");
    }

    [Fact]
    public async Task DotnetGlobals_OnTheServerWithNothingRegistered_SaysSo()
    {
        // The old message told you to guard with isServerRender(), on the server, in a view that
        // was already server-rendered. Naming the real mistake is the point of the fix.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            "use server";
            import { dotnet } from "dotnet:globals";
            export default function Index() { return <p>{String(dotnet.Missing)}</p>; }
            """);

        await project.CompileAsync();

        var error = await Should.ThrowAsync<JsxRenderException>(() => project.RenderAsync("Home/Index"));

        error.Message.ShouldContain("registered no .NET globals");
        error.Message.ShouldNotContain("ran on the client");
    }
}
