using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Compilation;

public class TsConfigWriterTests
{
    private static (JsxCoreOptions Options, CompilationLayout Layout) Setup()
    {
        var options = new JsxCoreOptions();
        return (options, CompilationLayout.Create(options, Path.Combine(Path.GetTempPath(), "jsxcore-cfg")));
    }

    [Fact]
    public void Build_DefaultOptions_EmitsTheSettingsThatMakeBundlerFreeEsmWork()
    {
        var (options, layout) = Setup();

        var compilerOptions = TsConfigWriter.Build(options, layout)["compilerOptions"]!;

        compilerOptions["jsx"]!.GetValue<string>().ShouldBe("react-jsx");
        compilerOptions["jsxImportSource"]!.GetValue<string>().ShouldBe("preact");
        compilerOptions["module"]!.GetValue<string>().ShouldBe("esnext");

        // Without this, emitted imports would still say ".tsx" and no browser could load them.
        compilerOptions["rewriteRelativeImportExtensions"]!.GetValue<bool>().ShouldBeTrue();
        compilerOptions["allowImportingTsExtensions"]!.GetValue<bool>().ShouldBeTrue();

        // Without this, .jsx and .js views are silently excluded from the compilation.
        compilerOptions["allowJs"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Build_TypeCheckingIsOff_AddsNoCheck()
    {
        var (options, layout) = Setup();
        TsConfigWriter.Build(options, layout)["compilerOptions"]!["noCheck"].ShouldBeNull();

        options.TypeChecking = TypeCheckingMode.Off;
        TsConfigWriter.Build(options, layout)["compilerOptions"]!["noCheck"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Build_CompilerOptionIsSuppliedByTheUser_Overrides()
    {
        var (options, layout) = Setup();
        options.CompilerOptions["strict"] = false;
        options.CompilerOptions["target"] = "es2017";

        var compilerOptions = TsConfigWriter.Build(options, layout)["compilerOptions"]!;

        compilerOptions["strict"]!.GetValue<bool>().ShouldBeFalse();
        compilerOptions["target"]!.GetValue<string>().ShouldBe("es2017");
    }

    [Fact]
    public void Build_SeveralViewExtensions_IncludesEveryOne()
    {
        var (options, layout) = Setup();

        var includes = TsConfigWriter.Build(options, layout)["include"]!.AsArray()
            .Select(node => node!.GetValue<string>()).ToList();

        includes.ShouldContain(i => i.EndsWith("*.tsx"));
        includes.ShouldContain(i => i.EndsWith("*.jsx"));
    }
}
