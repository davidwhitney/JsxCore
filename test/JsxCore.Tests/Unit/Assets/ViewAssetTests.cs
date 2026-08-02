using JsxCore.Compilation.Assets;
using Shouldly;

namespace JsxCore.Tests.Unit.Assets;

/// <summary>
/// What a <c>dotnet:wwwroot/…</c> specifier means, and what the renderer makes of the graph
/// recorded for it.
/// </summary>
public class ViewAssetTests
{
    [Theory]
    [InlineData("dotnet:wwwroot/images/logo.svg", "images/logo.svg")]
    [InlineData("dotnet:wwwroot/app.css", "app.css")]
    [InlineData("dotnet:rendering", null)]
    [InlineData("dotnet:globals", null)]
    [InlineData("./logo.svg", null)]
    [InlineData("/images/logo.svg", null)]
    [InlineData("preact", null)]
    [InlineData("dotnet:wwwroot/", null)]
    // Suffixes are Vite's, and are a separate feature: without them a module would be written that
    // the rewritten import does not name.
    [InlineData("dotnet:wwwroot/logo.svg?url", null)]
    [InlineData("dotnet:wwwroot/logo.svg#icon", null)]
    public void PathFor_TellsAnAssetImportFromEverythingElse(string specifier, string? expected) =>
        ViewAssets.PathFor(specifier).ShouldBe(expected);

    [Fact]
    public void Declarations_CoverEveryImportableExtension()
    {
        var declarations = ViewAssets.DeclarationSource();

        foreach (var extension in ViewAssets.ContentTypes.Keys)
        {
            declarations.ShouldContain($"declare module \"dotnet:wwwroot/*{extension}\"");
        }

        // One star per pattern is all TypeScript allows, and it matches separators, so one
        // declaration per extension covers every directory depth.
        declarations.ShouldNotContain("**");
    }

    [Fact]
    public void ModuleSource_ExportsTheUrlAsAConstant()
    {
        var module = ViewAssets.ModuleSource("/images/logo.svg");

        module.ShouldContain("\"/images/logo.svg\"");
        module.ShouldContain("export default url;");
    }

    [Fact]
    public void ContentTypes_AreCompressibleOnlyWhereCompressionHelps()
    {
        ViewAssets.IsCompressible(ViewAssets.ContentTypes[".css"]).ShouldBeTrue();
        ViewAssets.IsCompressible(ViewAssets.ContentTypes[".svg"]).ShouldBeTrue();

        // Already compressed: doing it again costs CPU to add a few bytes.
        ViewAssets.IsCompressible(ViewAssets.ContentTypes[".png"]).ShouldBeFalse();
        ViewAssets.IsCompressible(ViewAssets.ContentTypes[".woff2"]).ShouldBeFalse();
        ViewAssets.IsCompressible(ViewAssets.ContentTypes[".mp4"]).ShouldBeFalse();
    }

    private static ViewAssetManifest Manifest(params (string Module, string[] Imports, string[] Styles)[] modules)
    {
        var manifest = new ViewAssetManifest();
        foreach (var (module, imports, styles) in modules)
        {
            manifest.Modules[module] = new ViewAssetModule(imports, styles);
        }

        return manifest;
    }

    [Fact]
    public void StylesFor_EmitsDependenciesFirst()
    {
        // A component's styles before the page's, so the page can override them. Imports are
        // recorded relative to the compiled output rather than as the specifier was written, so
        // one module is one key however many views reach it.
        var manifest = Manifest(
            ("Home/Index.js", ["Shared/Card.js"], ["/page.css"]),
            ("Shared/Card.js", [], ["/card.css"]));

        manifest.StylesFor("Home/Index.js").ShouldBe(["/card.css", "/page.css"]);
    }

    [Fact]
    public void StylesFor_SharedStylesheet_AppearsOnce()
    {
        var manifest = Manifest(
            ("Home/Index.js", ["Shared/A.js", "Shared/B.js"], []),
            ("Shared/A.js", [], ["/theme.css", "/a.css"]),
            ("Shared/B.js", [], ["/theme.css", "/b.css"]));

        manifest.StylesFor("Home/Index.js").ShouldBe(["/theme.css", "/a.css", "/b.css"]);
    }

    [Fact]
    public void StylesFor_ImportCycle_Terminates()
    {
        // Circular imports are legal JavaScript, so this stops rather than fails.
        var manifest = Manifest(
            ("A.js", ["B.js"], ["/a.css"]),
            ("B.js", ["A.js"], ["/b.css"]));

        manifest.StylesFor("A.js").ShouldBe(["/b.css", "/a.css"]);
    }

    [Fact]
    public void StylesFor_AViewThatImportsNone_IsEmpty() =>
        Manifest(("Home/Index.js", [], ["/page.css"])).StylesFor("Home/Other.js").ShouldBeEmpty();

    [Fact]
    public void Manifest_RoundTripsThroughJson()
    {
        var manifest = Manifest(("Home/Index.js", ["Shared/Card.js"], ["/page.css"]));

        var parsed = ViewAssetManifest.Parse(manifest.ToJson());

        parsed.StylesFor("Home/Index.js").ShouldBe(["/page.css"]);
        parsed.Modules["Home/Index.js"].Imports.ShouldBe(["Shared/Card.js"]);
    }

    [Fact]
    public void Manifest_ThatIsNotJson_IsIgnoredRatherThanThrown() =>
        ViewAssetManifest.Parse("{ half a fi").Modules.ShouldBeEmpty();
}
