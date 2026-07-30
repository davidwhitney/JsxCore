using JsxCore.Compilation.Modules;
using JsxCore.Rendering;
using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Unit.Rendering;

/// <summary>
/// Packages built for a browser or for Node reach for globals the embedded engine does not have,
/// while their own module body runs rather than when something calls into them.
/// </summary>
/// <remarks>
/// React is the case that forced this: react-dom's server build constructs a MessageChannel and a
/// TextEncoder at module scope, and every React entry point reads process.env.NODE_ENV to choose
/// between its development and production build. Without the shims none of it evaluates at all.
/// </remarks>
[Trait("Category", "Network")]
public class HostShimTests
{
    private static async Task<string> RenderAsync(string view)
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.EnableReactCompatibility = false;
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        project.AddView("Home/Index.tsx", view);

        await project.CompileAsync();

        var renderer = new JsxServerRenderer(
            project.Options,
            project.Compilation,
            project.RuntimeLayout,
            new NodeModuleResolver(JsxProjectFixture.RepositoryRoot()));

        var result = await renderer.RenderAsync(
            project.Locate("Home/Index"), null, new Dictionary<string, object?>(),
            new ServiceCollection().BuildServiceProvider());

        return result.Html;
    }

    [Fact]
    public async Task Render_ViewImportsReactDomServer_EvaluatesRatherThanFailingOnAMissingGlobal()
    {
        // react-dom/server.browser pulls in the streaming renderer, which is what needs
        // MessageChannel and TextEncoder to exist before it will finish evaluating.
        var html = await RenderAsync("""
            import { renderToString } from "react-dom/server.browser";

            export default function Index() {
                return <p>{typeof renderToString}</p>;
            }
            """);

        html.ShouldContain("function");
    }

    [Fact]
    public async Task Render_ViewReadsProcessEnv_SeesAProductionBuild()
    {
        // React's entry points branch on this at module scope to pick a build.
        var html = await RenderAsync("""
            export default function Index() {
                return <p>{process.env.NODE_ENV}</p>;
            }
            """);

        html.ShouldContain("production");
    }

    [Fact]
    public async Task Render_ViewUsesTextEncoder_ProducesUtf8Bytes()
    {
        var html = await RenderAsync("""
            export default function Index() {
                const bytes = new TextEncoder().encode("hé");
                return <p>{Array.from(bytes).join(",")}</p>;
            }
            """);

        // "h" is one byte, "é" is two: 0x68, 0xC3, 0xA9.
        html.ShouldContain("104,195,169");
    }
}
