using JsxCore.Compilation.Assets;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// Linking the same output more than once.
/// </summary>
/// <remarks>
/// The linker rewrites the modules it reads, so its input on a second run is its own output from
/// the first. That used to be destructive: the rewritten specifiers gave it nothing to regenerate,
/// and the sweep that removes what nothing imports any more removed everything it had produced.
/// A stylesheet survived one <c>dotnet jsxcore assets</c> and disappeared on the next.
/// </remarks>
public class RelinkTests
{
    private static void WriteCompiledModule(JsxProjectFixture project, string relativePath, string contents)
    {
        var path = Path.Combine(
            project.Layout.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static ViewAssetLinker.Result Link(JsxProjectFixture project) =>
        ViewAssetLinker.Link(
            project.Layout,
            EsbuildToolchainLocator.Locate(JsxProjectFixture.RepositoryRoot()),
            npm: null);

    private static JsxProjectFixture ProjectWithAStylesheetAndAnAsset()
    {
        JsxProjectFixture.EnsureRepositoryPackages();

        var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", "<svg/>");
        project.AddView("Home/page.css", ".page { color: rebeccapurple; }");
        WriteCompiledModule(project, "Home/Index.js", """
            import "./page.css";
            import logo from "/images/logo.svg";
            export default function Index() { return logo; }
            """);

        return project;
    }

    [Fact]
    public void Link_RunTwice_KeepsWhatTheFirstRunProduced()
    {
        using var project = ProjectWithAStylesheetAndAnAsset();

        Link(project).Linked.ShouldBe(2);

        var stylesheet = ViewAssets.PathUnder(
            project.Layout.OutputDirectory, ViewAssets.StyleDirectory + "/Home/page.css");
        var module = ViewAssets.PathUnder(
            project.Layout.OutputDirectory, ViewAssets.ModuleDirectory + "/assets/images/logo.svg.js");

        File.Exists(stylesheet).ShouldBeTrue();
        File.Exists(module).ShouldBeTrue();

        // Nothing compiled in between, so the second run has nothing to link and nothing to remove.
        Link(project).Linked.ShouldBe(0);

        File.Exists(stylesheet).ShouldBeTrue();
        File.Exists(module).ShouldBeTrue();
    }

    [Fact]
    public void Link_RunTwice_KeepsTheStylesheetOnThePage()
    {
        // The files surviving is not enough: the document links what the manifest records, so a
        // second run that emptied it would serve an unstyled page from an intact stylesheet.
        using var project = ProjectWithAStylesheetAndAnAsset();

        var first = Link(project).Manifest.StylesFor("Home/Index.js");
        first.ShouldHaveSingleItem();

        Link(project).Manifest.StylesFor("Home/Index.js").ShouldBe(first);
    }

    [Fact]
    public void Link_AfterAViewStopsImportingAStylesheet_StillRemovesIt()
    {
        // The sweep still has to work. What distinguishes this from a second run is that the module
        // was compiled again, so its specifiers arrive raw rather than already rewritten.
        using var project = ProjectWithAStylesheetAndAnAsset();

        Link(project);

        var stylesheet = ViewAssets.PathUnder(
            project.Layout.OutputDirectory, ViewAssets.StyleDirectory + "/Home/page.css");

        File.Exists(stylesheet).ShouldBeTrue();

        WriteCompiledModule(project, "Home/Index.js", "export default function Index() { return null; }");

        Link(project);

        File.Exists(stylesheet).ShouldBeFalse();
    }
}
