using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Rendering;
using Shouldly;

namespace JsxCore.Tests.Unit.Rendering;

/// <summary>
/// <c>@jsxcore/client</c>, the client entry point under a name that does not say which framework.
/// </summary>
/// <remarks>
/// Switching <c>&lt;JsxCoreFramework&gt;</c> is a supported thing to do, and it used to move every
/// name that reaches the mounting entry: code written against <c>@jsxcore/preact/client</c> stopped
/// resolving the moment a project moved to React.
/// </remarks>
public class NeutralClientSpecifierTests
{
    [Fact]
    public void Modules_UnderReact_PointTheNeutralNameAtReactsEntry()
    {
        var layout = JsxRuntimeLayout.React(new ReactEntryStager(TestLayout()));

        layout.ResolveModule(RuntimeAssets.ClientSpecifier).ShouldBe("client.js");
        layout.AssetSegment.ShouldBe("react");

        // The same file the framework-specific name reaches, so the two cannot drift.
        layout.ResolveModule(RuntimeAssets.ClientSpecifier)
            .ShouldBe(layout.ResolveModule("@jsxcore/react/client"));
    }

    [Fact]
    public void ImportMap_UnderReact_ServesTheNeutralNameFromReactsDirectory()
    {
        var layout = JsxRuntimeLayout.React(new ReactEntryStager(TestLayout()));

        var map = layout.BuildImportMap("/_jsx/vabc");

        map[RuntimeAssets.ClientSpecifier].ShouldBe("/_jsx/vabc/react/client.js");
        map[RuntimeAssets.ClientSpecifier].ShouldBe(map["@jsxcore/react/client"]);
    }

    [Fact]
    public void EmittedDocument_UnderEitherFramework_ImportsTheNeutralName()
    {
        // What the browser receives should read the same either way. A framework's name in the
        // markup is a detail of what is serving the page, and the import map already carries it for
        // anyone looking.
        var react = JsxRuntimeLayout.React(new ReactEntryStager(TestLayout()));

        react.ClientSpecifier.ShouldBe(RuntimeAssets.ClientSpecifier);
        react.BuildImportMap("/_jsx/vabc").ShouldContainKey(react.ClientSpecifier);
    }

    [Fact]
    public void Declarations_AreEmbedded_SoTheNameTypeChecks()
    {
        // A specifier the import map resolves and TypeScript does not is half a feature: the view
        // would run and refuse to compile.
        var declarations = RuntimeAssets.TryGetText(RuntimeAssets.ClientDeclarations + ".d.ts");

        declarations.ShouldNotBeNull();
        declarations.ShouldContain("mountView");

        // Deliberately narrow. Preact's entry also exports render and hydrate; React's does not, so
        // declaring them would promise an export that exists under one framework and not the other.
        declarations.ShouldNotContain("export declare function render");
        declarations.ShouldNotContain("export declare function hydrate");
    }

    private static CompilationLayout TestLayout() =>
        CompilationLayout.Create(
            new JsxCoreOptions(),
            Path.Combine(Path.GetTempPath(), "jsxcore-neutral-" + Guid.NewGuid().ToString("n")[..8]));
}
