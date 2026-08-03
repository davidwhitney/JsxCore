using JsxCore.Compilation.Assets;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// What the linker does to a compiled module, run against real compiler output.
/// </summary>
public class LinkedModuleTests
{
    private static async Task<string> LinkAndRead(JsxProjectFixture project, string module)
    {
        await project.CompileAsync();
        ViewAssetLinker.Link(project.Layout);

        return await File.ReadAllTextAsync(Path.Combine(
            project.Layout.OutputDirectory, module.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task AnImportSampleAViewDisplays_IsLeftAlone()
    {
        // A page showing people how to import an asset is displaying text, not importing anything.
        // This used to be rewritten, and a module generated for it, because the specifier was found
        // by pattern rather than by position.
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", "<svg/>");
        project.AddView("Home/Index.tsx", """
            export default function Index() {
                return <code>{'import "/images/logo.svg"'}</code>;
            }
            """);

        var emitted = await LinkAndRead(project, "Home/Index.js");

        emitted.ShouldContain("import \"/images/logo.svg\"");
        emitted.ShouldNotContain("_static");
    }

    [Fact]
    public async Task ARealImportBesideASample_IsStillLinked()
    {
        // The sample must survive without the real import being missed.
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", "<svg/>");
        project.AddView("Home/Index.tsx", """
            import logo from "/images/logo.svg";

            export default function Index() {
                return <><img src={logo} /><code>{'import "/images/other.svg"'}</code></>;
            }
            """);

        var emitted = await LinkAndRead(project, "Home/Index.js");

        // Rewritten to the generated module.
        emitted.ShouldContain("_static/images/logo.svg.js");

        // And the sample is untouched, so it still names the file it was talking about.
        emitted.ShouldContain("/images/other.svg");
        emitted.ShouldNotContain("_static/images/other.svg.js");
    }

    [Fact]
    public async Task AnAssetImportInACommentedOutLine_IsNotLinked()
    {
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", "<svg/>");
        project.AddView("Home/Index.tsx", """
            // import logo from "/images/logo.svg";
            export default function Index() { return <p>none</p>; }
            """);

        var linked = await LinkAndRead(project, "Home/Index.js");

        linked.ShouldNotContain("_static");
    }
}
