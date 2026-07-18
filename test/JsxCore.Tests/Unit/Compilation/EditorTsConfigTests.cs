using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Unit.Compilation;

public class EditorTsConfigTests
{
    [Fact]
    public async Task WriteIdeConfig_ViewsDirectoryExists_WritesAConfigEditorsCanUse()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");

        await project.CompileAsync();

        var path = Path.Combine(project.ViewsDirectory, "tsconfig.json");
        File.Exists(path).ShouldBeTrue();

        var contents = await File.ReadAllTextAsync(path);
        contents.ShouldContain(TsConfigWriter.GeneratedMarker);
        contents.ShouldContain("@jsxcore/runtime");

        // The mapping must be relative to the views directory, not absolute.
        contents.ShouldContain("../obj/JsxCore/runtime/index.d.ts");
        contents.ShouldNotContain(project.Root);
    }

    [Fact]
    public async Task WriteIdeConfig_DeveloperHasTakenOverTheFile_LeavesItAlone()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");

        var path = Path.Combine(project.ViewsDirectory, "tsconfig.json");
        const string handWritten = """{ "compilerOptions": { "strict": false } }""";
        await File.WriteAllTextAsync(path, handWritten);

        await project.CompileAsync();

        (await File.ReadAllTextAsync(path)).ShouldBe(handWritten);
    }

    [Fact]
    public async Task WriteIdeConfig_GenerationIsDisabled_WritesNothing()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.GenerateEditorTsConfig = false;
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");

        await project.CompileAsync();

        File.Exists(Path.Combine(project.ViewsDirectory, "tsconfig.json")).ShouldBeFalse();
    }

    [Fact]
    public void WriteIdeConfig_ComparedWithTheBuildConfig_AgreesOnWhatMatters()
    {
        var options = new JsxCoreOptions();
        var layout = CompilationLayout.Create(options, Path.Combine(Path.GetTempPath(), "jsxcore-cfg"));

        var compiler = TsConfigWriter.Build(options, layout)["compilerOptions"]!;
        var editor = TsConfigWriter.BuildIdeConfig(options, layout)["compilerOptions"]!;

        // Both are derived from the same base file; if they diverged, an editor would report
        // errors the compiler does not, or the reverse.
        foreach (var key in new[] { "jsx", "jsxImportSource", "module", "moduleResolution", "strict" })
        {
            editor[key]!.ToJsonString().ShouldBe(compiler[key]!.ToJsonString(), key);
        }

        foreach (var key in new[] { "rewriteRelativeImportExtensions", "allowImportingTsExtensions", "allowJs" })
        {
            editor[key]!.GetValue<bool>().ShouldBeTrue(key);
        }

        // The editor never emits; that is the compiler's job.
        editor["noEmit"]!.GetValue<bool>().ShouldBeTrue();
    }
}
