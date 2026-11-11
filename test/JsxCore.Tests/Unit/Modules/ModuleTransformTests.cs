using JsxCore.Compilation.Modules;
using Shouldly;

namespace JsxCore.Tests.Unit.Modules;

/// <summary>
/// Reshaping a package file into a module the server engine and the browser can both load.
/// </summary>
public class ModuleTransformTests
{
    [Theory]
    [InlineData("""import "./a.css";""", "./a.css")]
    [InlineData("""import"./a.css";""", "./a.css")]
    [InlineData("""import'./a.css';""", "./a.css")]
    [InlineData("""import{a}from"./a.js";""", "./a.js")]
    public void FindSpecifiers_ImportWithOrWithoutSpace_FindsTheSpecifier(string source, string expected) =>
        ModuleTransform.FindSpecifiers(source).ShouldHaveSingleItem().ShouldBe(expected);

    [Fact]
    public void Apply_Stylesheet_IsAnEmptyModule()
    {
        var shaped = ModuleTransform.Apply(
            "/node_modules/pkg/a.css", NodeModuleKind.Stylesheet, ".a { color: red; }", new NoRewrites());

        shaped.Source.ShouldBe("export {};");
        shaped.Dependencies.ShouldBeEmpty();
    }

    private sealed class NoRewrites : ISpecifierRewriter
    {
        public RewrittenSpecifier? Rewrite(string specifier, string importerPath, ModuleReference reference) => null;
    }
}
