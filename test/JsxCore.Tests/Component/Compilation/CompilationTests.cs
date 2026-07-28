using JsxCore.Compilation;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Compilation;

public class CompilationTests
{
    [Fact]
    public async Task Compile_TsxView_ProducesAnEsModule()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <h1>Hello {model.name}</h1>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());

        var emitted = await File.ReadAllTextAsync(Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));
        emitted.ShouldContain("""from "@jsxcore/runtime/jsx-runtime";""");
        emitted.ShouldContain("export default function Index");
    }

    [Fact]
    public async Task Compile_RelativeTsxImport_IsRewrittenSoBrowsersCanResolveIt()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", """
            export function Card({ title }: { title: string }) {
                return <section>{title}</section>;
            }
            """);
        project.AddView("Home/Index.tsx", """
            import { Card } from "../Shared/Card.tsx";
            export default function Index() {
                return <Card title="hi" />;
            }
            """);

        await project.CompileAsync();

        var emitted = await File.ReadAllTextAsync(Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));

        // This rewrite is what removes the need for a bundler: the emitted specifier is one the
        // browser can fetch directly.
        emitted.ShouldContain("""from "../Shared/Card.js";""");
        emitted.ShouldNotContain(".tsx");
    }

    [Fact]
    public async Task Compile_JsxView_ProducesAnEsModule()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Plain.jsx", """
            export default function Plain({ model }) {
                return <p>{model.text}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
        File.Exists(Path.Combine(project.Layout.OutputDirectory, "Home", "Plain.js")).ShouldBeTrue();
    }

    [Fact]
    public async Task Compile_TypeErrorsInWarnMode_ReportsDiagnosticsAndStillEmits()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Broken.tsx", """
            export default function Broken() {
                const value: number = "not a number";
                return <p>{value}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeFalse();
        build.Result.Errors.ShouldContain(d => d.Code == "TS2322");
        build.Result.Errors[0].FilePath.ShouldNotBeNull();
        build.Result.Errors[0].Line.ShouldBe(2);

        // Warn mode keeps serving, so the emit must still be there.
        File.Exists(Path.Combine(project.Layout.OutputDirectory, "Home", "Broken.js")).ShouldBeTrue();
    }

    [Fact]
    public async Task Compile_TypeErrorsInErrorMode_Throws()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Error;
        project.AddView("Home/Broken.tsx", """
            export default function Broken() {
                const value: number = "not a number";
                return <p>{value}</p>;
            }
            """);

        await project.Compilation.InitialiseAsync();

        var exception = await Should.ThrowAsync<JsxCompilationException>(() => project.Compilation.CompileAsync());
        exception.Diagnostics.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Compile_TypeCheckingIsOff_SkipsCheckingAndEmits()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Broken.tsx", """
            export default function Broken() {
                const value: number = "not a number";
                return <p>{value}</p>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());
        File.Exists(Path.Combine(project.Layout.OutputDirectory, "Home", "Broken.js")).ShouldBeTrue();
    }

    [Fact]
    public async Task BuildId_SourcesChange_ChangesAndIsOtherwiseStable()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>one</p>; }");

        var first = await project.CompileAsync();
        var unchanged = await project.Compilation.CompileAsync();
        unchanged.BuildId.ShouldBe(first.BuildId);

        project.AddView("Home/Index.tsx", "export default function Index() { return <p>two</p>; }");
        var changed = await project.Compilation.CompileAsync();

        changed.BuildId.ShouldNotBe(first.BuildId);
    }

    [Fact]
    public async Task RuntimeAssets_AfterCompiling_WritesDeclarationsButNotJavaScript()
    {
        using var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");

        await project.CompileAsync();

        var runtimeDirectory = project.Layout.RuntimeDirectory;
        File.Exists(Path.Combine(runtimeDirectory, "jsx-runtime.d.ts")).ShouldBeTrue();

        // Runtime JavaScript is served from embedded resources, so it must never hit the disk of a
        // consuming project.
        Directory.GetFiles(runtimeDirectory, "*.js").ShouldBeEmpty();
    }
}
