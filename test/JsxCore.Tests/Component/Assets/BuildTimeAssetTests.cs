using System.Text.RegularExpressions;
using JsxCore.Compilation.Assets;
using JsxCore.Tests.Fixtures;
using JsxCore.Tool;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// The build-time half of asset linking, which is what a published application serves.
/// </summary>
/// <remarks>
/// Nothing recompiles a published application, so whatever this leaves on disk is final. It had no
/// tests, and was passing neither esbuild nor the package resolver: stylesheets were left exactly as
/// the compiler wrote them, and every deployment that did not compile at startup served views whose
/// stylesheet imports resolved to nothing.
/// </remarks>
public class BuildTimeAssetTests
{
    /// <summary>
    /// Writes the module the compiler would have emitted, rather than compiling.
    /// </summary>
    /// <remarks>
    /// Compiling through the fixture links as well, and linking twice over one set of modules is
    /// not the same run twice: the second pass sees specifiers the first already rewrote, so it has
    /// nothing to generate and prunes what the first produced. The build does exactly one link,
    /// which is what this reproduces.
    /// </remarks>
    private static void WriteCompiledModule(JsxProjectFixture project, string relativePath, string contents)
    {
        var path = Path.Combine(
            project.Layout.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static int RunAssetsCommand(JsxProjectFixture project)
    {
        // A real build finds esbuild in the project's own node_modules, which a throwaway project
        // does not have. Named explicitly here, through the option the build uses when it is
        // configured rather than discovered.
        var esbuild = EsbuildToolchainLocator.Locate(JsxProjectFixture.RepositoryRoot());

        return AssetsCommand.Run(Arguments.Parse([
            "--project-dir", project.Root,
            "--views", project.Layout.ViewsDirectory,
            "--working", project.Layout.WorkingDirectory,
            "--web-root", project.Layout.WebRoot,
            "--esbuild", esbuild!.ExecutablePath
        ]));
    }

    [Fact]
    public async Task Stylesheet_LinkedByTheBuild_IsProcessedAndServable()
    {
        JsxProjectFixture.EnsureRepositoryPackages();

        using var project = JsxProjectFixture.Create();
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        project.AddView("Home/Card.module.css", ".card { padding: 24px; }");
        WriteCompiledModule(project, "Home/Index.js", """
            import styles from "./Card.module.css";
            export default function Index() { return styles.card; }
            """);

        RunAssetsCommand(project).ShouldBe(0);

        var stylesheet = ViewAssets.PathUnder(
            project.Layout.OutputDirectory, ViewAssets.StyleDirectory + "/Home/Card.module.css");

        File.Exists(stylesheet).ShouldBeTrue();

        // Scoped, so the markup and the stylesheet agree, which is the part that cannot be faked.
        // The name it becomes is esbuild's to choose; what matters is that it is no longer ".card".
        var css = await File.ReadAllTextAsync(stylesheet);
        css.ShouldContain("padding: 24px");
        Regex.IsMatch(css, @"\.card\s*\{").ShouldBeFalse();

        var emitted = await File.ReadAllTextAsync(
            Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));

        emitted.ShouldNotContain("./Card.module.css");
    }

    [Fact]
    public async Task Assets_LinkedByTheBuild_StillResolve()
    {
        using var project = JsxProjectFixture.Create();
        project.AddFile("wwwroot/images/logo.svg", "<svg/>");
        WriteCompiledModule(project, "Home/Index.js", """
            import logo from "/images/logo.svg";
            export default function Index() { return logo; }
            """);

        RunAssetsCommand(project).ShouldBe(0);

        var emitted = await File.ReadAllTextAsync(
            Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));

        emitted.ShouldContain("_dist/modules/assets/images/logo.svg.js");
    }
}
