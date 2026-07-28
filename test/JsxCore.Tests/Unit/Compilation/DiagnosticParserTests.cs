using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Compilation;

public class DiagnosticParserTests
{
    [Fact]
    public void Parse_DiagnosticHasAFileAndPosition_ReadsAllOfIt()
    {
        var diagnostics = DiagnosticParser.Parse(
            "Views/Home/Index.tsx(12,7): error TS2322: Type 'string' is not assignable to type 'number'.");

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.FilePath.ShouldBe("Views/Home/Index.tsx");
        diagnostic.Line.ShouldBe(12);
        diagnostic.Column.ShouldBe(7);
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Code.ShouldBe("TS2322");
        diagnostic.Message.ShouldBe("Type 'string' is not assignable to type 'number'.");
    }

    [Fact]
    public void Parse_DiagnosticHasNoFile_IsStillRead()
    {
        var diagnostics = DiagnosticParser.Parse("error TS5083: Cannot read file 'tsconfig.json'.");

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.FilePath.ShouldBeNull();
        diagnostic.Code.ShouldBe("TS5083");
    }

    [Fact]
    public void Parse_MessageSpansSeveralLines_FoldsThemIntoOne()
    {
        var diagnostics = DiagnosticParser.Parse(
            "a.tsx(1,1): error TS2345: Argument mismatch.\n  Types of property 'x' are incompatible.");

        diagnostics.ShouldHaveSingleItem().Message.ShouldContain("Types of property 'x' are incompatible.");
    }

    [Fact]
    public void Parse_OutputIsNoiseOrEmpty_ProducesNoDiagnostics()
    {
        DiagnosticParser.Parse("").ShouldBeEmpty();
        DiagnosticParser.Parse("Compiling...\nDone.").ShouldBeEmpty();
    }

    [Fact]
    public void Parse_MixedSeverities_DistinguishesWarningsFromErrors()
    {
        var diagnostics = DiagnosticParser.Parse(
            "a.tsx(1,1): error TS1005: broken.\nb.tsx(2,2): warning TS6133: careful.");

        diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error).ShouldBe(1);
        diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning).ShouldBe(1);
    }
}
