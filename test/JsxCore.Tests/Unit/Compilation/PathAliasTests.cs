using System.Text.Json;
using JsxCore.Compilation;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Unit.Compilation;

/// <summary>
/// What happens when an application adds its own <c>paths</c> through
/// <see cref="JsxCoreOptions.CompilerOptions"/>, which is how someone would ask for the
/// <c>@/components/*</c> alias every Next.js and Vite template ships with.
/// </summary>
public class PathAliasTests
{
    private static JsxCoreOptions WithAlias()
    {
        var options = new JsxCoreOptions();
        options.CompilerOptions["paths"] = new Dictionary<string, string[]>
        {
            ["@/*"] = ["../../Views/*"]
        };

        return options;
    }

    [Fact]
    public void UserPaths_MergeOverTheGeneratedOnes()
    {
        using var project = JsxProjectFixture.Create();
        var layout = CompilationLayout.Create(WithAlias(), project.Root);

        var paths = TsConfigWriter.Build(WithAlias(), layout)["compilerOptions"]!["paths"]!.AsObject();

        paths.ContainsKey("@/*").ShouldBeTrue();

        // Assigning paths wholesale used to drop every mapping JsxCore had just made, so adding
        // one alias silently stopped the dotnet: schemes and the framework's types resolving.
        paths.ContainsKey("dotnet:rendering").ShouldBeTrue();
        paths.ContainsKey("preact").ShouldBeTrue();
    }

    [Fact]
    public void TheViewsAlias_IsGeneratedWithoutBeingAskedFor()
    {
        using var project = JsxProjectFixture.Create();
        var options = new JsxCoreOptions();
        var layout = CompilationLayout.Create(options, project.Root);

        var paths = TsConfigWriter.Build(options, layout)["compilerOptions"]!["paths"]!.AsObject();

        paths.ContainsKey("@/*").ShouldBeTrue();
    }

    [Fact]
    public void GeneratedPaths_WithoutUserOverrides_MapTheSchemes()
    {
        using var project = JsxProjectFixture.Create();
        var options = new JsxCoreOptions();
        var layout = CompilationLayout.Create(options, project.Root);

        var paths = TsConfigWriter.Build(options, layout)["compilerOptions"]!["paths"]!.AsObject();

        paths.ContainsKey("dotnet:rendering").ShouldBeTrue();
        paths.ContainsKey("dotnet:rendering/head").ShouldBeTrue();
        paths.ContainsKey("preact").ShouldBeTrue();
    }

    [Fact]
    public async Task AnAliasedImport_IsRewrittenToSomethingTheBrowserCanLoad()
    {
        // rewriteRelativeImportExtensions does what it says: it rewrites *relative* specifiers. An
        // aliased one comes out of tsc exactly as written, still carrying .tsx, so the linker has
        // to turn it into a relative path to the compiled module.
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", "export function Card() { return <p>card</p>; }");
        project.AddView("Home/Index.tsx", """
            import { Card } from "@/Shared/Card.tsx";
            export default function Index() { return <Card />; }
            """);

        await project.CompileAsync();

        var emitted = await File.ReadAllTextAsync(
            Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));

        emitted.ShouldNotContain("@/");
        emitted.ShouldContain("../Shared/Card.js");
    }

    [Fact]
    public async Task AnAliasedImport_RendersOnTheServerToo()
    {
        // The server module loader resolves the same rewritten specifier the browser does, so this
        // is the check that one rewrite serves both.
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", "export function Card() { return <p>card</p>; }");
        project.AddView("Home/Index.tsx", """
            "use server";
            import { Card } from "@/Shared/Card.tsx";
            export default function Index() { return <Card />; }
            """);

        await project.CompileAsync();
        var result = await project.RenderAsync("Home/Index");

        result.Html.ShouldBe("<p>card</p>");
    }
}
