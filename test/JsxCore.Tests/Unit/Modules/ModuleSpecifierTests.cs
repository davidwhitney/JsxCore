using JsxCore.Compilation.Modules;
using Shouldly;

namespace JsxCore.Tests.Unit.Modules;

/// <summary>
/// Finding the specifiers a compiled module imports from, and only those.
/// </summary>
/// <remarks>
/// The cases that matter are the ones a regular expression gets wrong: text that reads like an
/// import but is a string, a comment, or a property named <c>from</c>.
/// </remarks>
public class ModuleSpecifierTests
{
    private static IReadOnlyList<string> Scan(string source) =>
        ModuleSpecifiers.Scan(source).Select(specifier => specifier.Value).ToList();

    [Theory]
    [InlineData("""import { Card } from "../Shared/Card.js";""", "../Shared/Card.js")]
    [InlineData("""import "./side-effect.css";""", "./side-effect.css")]
    [InlineData("""export { Card } from "./Card.js";""", "./Card.js")]
    [InlineData("""export * from "./all.js";""", "./all.js")]
    [InlineData("""const m = await import("./lazy.js");""", "./lazy.js")]
    [InlineData("""import styles from './Card.module.css';""", "./Card.module.css")]
    [InlineData("import x from\n    \"./wrapped.js\";", "./wrapped.js")]
    public void Finds_TheSpecifier(string source, string expected) =>
        Scan(source).ShouldHaveSingleItem().ShouldBe(expected);

    [Fact]
    public void Ignores_ASpecifierInsideAStringLiteral()
    {
        // The case that shipped broken: a page displaying an import example had the example
        // rewritten, and a module generated for it.
        Scan("""const sample = 'import "/images/logo.svg"';""").ShouldBeEmpty();
        Scan("""const sample = "import './styles.css'";""").ShouldBeEmpty();
    }

    [Fact]
    public void Ignores_ASpecifierInsideATemplateLiteral() =>
        Scan("""const sample = `import "./styles.css"`;""").ShouldBeEmpty();

    [Fact]
    public void Finds_ASpecifierInsideATemplateSubstitution() =>
        Scan("""const html = `<p>${await import("./lazy.js")}</p>`;""")
            .ShouldHaveSingleItem().ShouldBe("./lazy.js");

    [Fact]
    public void Handles_NestedTemplateSubstitutions() =>
        Scan("""
            const x = `a${`b${await import("./deep.js")}c`}d`;
            import "./real.js";
            """)
            .ShouldBe(["./deep.js", "./real.js"]);

    [Fact]
    public void Ignores_ASpecifierInAComment()
    {
        Scan("""// import "./commented.js";""").ShouldBeEmpty();
        Scan("""/* import "./commented.js"; */""").ShouldBeEmpty();
        Scan("""
            /**
             * import "./doc.css";
             */
            import "./real.js";
            """).ShouldHaveSingleItem().ShouldBe("./real.js");
    }

    [Fact]
    public void Ignores_APropertyNamedFrom() =>
        // Array.from("abc") is a call, not an import of "abc".
        Scan("""const chars = Array.from("abc");""").ShouldBeEmpty();

    [Fact]
    public void Ignores_ImportMeta() =>
        Scan("""const url = import.meta.url; const s = "./not-an-import.js";""").ShouldBeEmpty();

    [Fact]
    public void Handles_ARegexLiteralContainingQuotes() =>
        // Without regex handling the quote inside the character class opens a string, and the
        // real import that follows is swallowed.
        Scan("""
            const quoted = /["']/g;
            import "./after-regex.js";
            """).ShouldHaveSingleItem().ShouldBe("./after-regex.js");

    [Fact]
    public void Handles_DivisionThatIsNotARegex() =>
        Scan("""
            const ratio = width / height / 2;
            import "./after-division.js";
            """).ShouldHaveSingleItem().ShouldBe("./after-division.js");

    [Fact]
    public void Handles_AnEscapedQuoteInsideAString() =>
        Scan("""
            const s = "he said \"import\" loudly";
            import "./real.js";
            """).ShouldHaveSingleItem().ShouldBe("./real.js");

    [Fact]
    public void Rewrite_ReplacesOnlyWhatTheRewriterClaims()
    {
        const string source = """
            import { Card } from "../Shared/Card.js";
            import "./page.css";
            """;

        var rewritten = ModuleSpecifiers.Rewrite(
            source,
            ModuleSpecifiers.Scan(source),
            specifier => specifier.Value.EndsWith(".css", StringComparison.Ordinal)
                ? "./_dist/modules/styles/page.css.js"
                : null);

        rewritten.ShouldBe("""
            import { Card } from "../Shared/Card.js";
            import "./_dist/modules/styles/page.css.js";
            """);
    }

    [Fact]
    public void Rewrite_ReturnsTheOriginalWhenNothingChanged()
    {
        const string source = """import { Card } from "./Card.js";""";

        ModuleSpecifiers.Rewrite(source, ModuleSpecifiers.Scan(source), _ => null)
            .ShouldBeSameAs(source);
    }

    [Fact]
    public void Scan_HandlesAnUnterminatedString() =>
        // Not valid output, and must still terminate rather than run away.
        Scan("import \"./unterminated\nexport default 1;").ShouldBeEmpty();
}
