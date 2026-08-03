using JsxCore.Compilation.Assets;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Unit.Assets;

/// <summary>
/// What linking does when esbuild is not there.
/// </summary>
/// <remarks>
/// Scoping is the part that cannot be faked, and since esbuild became a correctness dependency
/// rather than an optimisation this is the path that decides whether a project without it is told
/// or is quietly served a page whose class names do not match its stylesheet.
/// <para>
/// The compiler is not run: these write the module tsc would have emitted, because the point is
/// what the linker does with it, and a real compile would link it with the esbuild the test
/// machine has.
/// </para>
/// </remarks>
public class MissingEsbuildTests
{
    private static void WriteCompiledModule(JsxProjectFixture project, string relativePath, string contents)
    {
        var path = Path.Combine(
            project.Layout.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static (IReadOnlyList<string> Reported, ViewAssetLinker.Result Result) LinkWithoutEsbuild(
        JsxProjectFixture project)
    {
        var reported = new List<string>();

        var result = ViewAssetLinker.Link(
            project.Layout.OutputDirectory,
            project.Layout.WebRoot,
            project.Layout.ViewsDirectory,
            aliases: null,
            esbuild: null,
            npm: null,
            report: reported.Add);

        return (reported, result);
    }

    [Fact]
    public void ACssModule_IsReportedRatherThanScopedWrongly()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Card.module.css", ".card { padding: 24px; }");
        WriteCompiledModule(project, "Home/Index.js", """
            import styles from "./Card.module.css";
            export default function Index() { return styles.card; }
            """);

        var (reported, result) = LinkWithoutEsbuild(project);

        reported.ShouldContain(message => message.Contains("esbuild", StringComparison.Ordinal));

        // Nothing was written that a page would link, and no scoped names were invented.
        Directory.Exists(Path.Combine(project.Layout.OutputDirectory, ViewStyles.DirectoryName))
            .ShouldBeFalse();

        result.Manifest.StylesFor("Home/Index.js").ShouldBeEmpty();
    }

    [Fact]
    public void APlainStylesheet_IsReportedTheSameWay()
    {
        // Not a module, so nothing needs scoping, but it still has to be copied somewhere servable
        // and esbuild is what copies it.
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/page.css", ".page { color: rebeccapurple; }");
        WriteCompiledModule(project, "Home/Index.js", """import "./page.css";""");

        var (reported, _) = LinkWithoutEsbuild(project);

        reported.ShouldContain(message => message.Contains("esbuild", StringComparison.Ordinal));
    }

    [Fact]
    public void AStylesheetInTheWebRoot_StillWorks()
    {
        // The application already serves it, so esbuild is not in the path at all and its absence
        // must not take this down with the rest.
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/css/site.css", "body { margin: 0; }");
        WriteCompiledModule(project, "Home/Index.js", """import "/css/site.css";""");

        var (reported, result) = LinkWithoutEsbuild(project);

        reported.ShouldBeEmpty();
        result.Manifest.StylesFor("Home/Index.js").ShouldBe(["/css/site.css"]);
    }
}
