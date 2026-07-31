using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

public class FrameworkHeaderTests
{
    private static JsxProjectFixture Project(JsFramework framework)
    {
        var project = JsxProjectFixture.Create(framework);
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>hi</p>; }
            """);
        return project;
    }

    private static string? Header(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-JsxCore-Framework", out var values) ? values.First() : null;

    private static async Task<string?> HeaderFor(JsFramework framework, string environment)
    {
        using var project = Project(framework);
        await project.CompileAsync();

        await using var host = await JsxTestHost.StartAsync(
            project,
            options => options.TypeDefinitions.ApplicationAssembly = FrameworkAssembly.For(framework),
            environment: environment);

        var html = await host.GetStringAsync("/client/Index");
        var module = html.Split('"').First(part => part.EndsWith("/views/Home/Index.js", StringComparison.Ordinal));

        return Header(await host.Client.GetAsync(module));
    }

    [Theory]
    [InlineData(JsFramework.Preact, "preact")]
    [InlineData(JsFramework.React, "react")]
    public async Task Assets_InDevelopment_SayWhichFrameworkServedThem(JsFramework framework, string expected) =>
        (await HeaderFor(framework, "Development")).ShouldBe(expected);

    [Fact]
    public async Task Pages_InDevelopment_SayItToo()
    {
        // The page is the request people actually look at, so an assets-only header is invisible
        // to anyone checking in a browser.
        using var project = Project(JsFramework.Preact);
        await project.CompileAsync();

        await using var host = await JsxTestHost.StartAsync(
            project,
            options => options.TypeDefinitions.ApplicationAssembly = FrameworkAssembly.For(JsFramework.Preact));

        Header(await host.Client.GetAsync("/client/Index")).ShouldBe("preact");
    }

    [Fact]
    public async Task Assets_OutsideDevelopment_SayNothing() =>
        // It describes the build rather than the request, so a public server has no reason to.
        (await HeaderFor(JsFramework.Preact, "Production")).ShouldBeNull();
}
