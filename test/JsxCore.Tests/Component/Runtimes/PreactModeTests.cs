using System.Net;
using JsxCore.Compilation;
using JsxCore.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Tests.Component.Runtimes;

/// <summary>
/// Exercises Preact mode against the real Preact packages. These tests matter because Preact mode
/// is the path most applications will take: the built-in runtime is deliberately small, and Preact
/// is what makes context, the full hook set and true hydration available.
/// </summary>
public class PreactModeTests
{
    private static JsxProjectFixture PreactProject()
    {
        var project = JsxProjectFixture.Create();

        // The fixture's content root is a temp directory, so point node_modules resolution at the
        // repository. This is the same setting a relocated test-host content root needs.
        project.Options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        return project;
    }

    private static PreactVendorStager StagerFor(JsxProjectFixture project) =>
        new(project.Layout,
            NodeModulesLayout.For(project.Root, project.Options.AdditionalToolchainSearchPaths),
            NullLogger<PreactVendorStager>.Instance);

    private static async Task<(JsxRuntimeLayout Runtime, JsxServerRenderer Renderer)> PrepareAsync(JsxProjectFixture project)
    {
        var stager = StagerFor(project);
        stager.Stage();
        var runtime = JsxRuntimeLayout.Preact(stager, project.Options.EnableReactCompatibility);
        await project.CompileAsync();
        return (runtime, new JsxServerRenderer(project.Options, project.Compilation, runtime));
    }

    private static Task<ServerRenderResult> RenderAsync(
        JsxServerRenderer renderer, JsxProjectFixture project, string view, object? model = null) =>
        renderer.RenderAsync(
            project.Locate(view),
            model,
            new Dictionary<string, object?>(),
            new ServiceCollection().BuildServiceProvider());

    [Fact]
    public void PreactStaging_Runs_CopiesPlainEsModulesWithNoBundling()
    {
        using var project = PreactProject();
        var stager = StagerFor(project);

        stager.Stage();

        stager.Staged.ShouldContainKey("preact");
        stager.Staged.ShouldContainKey("preact/hooks");
        stager.Staged.ShouldContainKey("preact/jsx-runtime");
        stager.Staged.ShouldContainKey("preact-render-to-string");

        var staged = await_(Path.Combine(stager.Directory, "preact.js"));
        staged.ShouldContain("export");

        // Copied verbatim from node_modules: the version installed is the version that runs.
        var source = stager.ResolveInNodeModules("preact/dist/preact.mjs")!;
        staged.ShouldBe(File.ReadAllText(source));

        static string await_(string path) => File.ReadAllText(path);
    }

    [Fact]
    public async Task Compile_PreactMode_UsesPreactsOwnJsxRuntime()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <h1>Hello {model.name}</h1>;
            }
            """);

        var build = await project.CompileAsync();

        build.Result.Succeeded.ShouldBeTrue(build.Result.FormatDiagnostics());

        var emitted = await File.ReadAllTextAsync(Path.Combine(project.Layout.OutputDirectory, "Home", "Index.js"));
        emitted.ShouldContain("preact/jsx-runtime");
        emitted.ShouldNotContain("dotnet:rendering");
    }

    [Fact]
    public async Task Render_PreactMode_ProducesMarkupOnTheServer()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                return <h1 class="title">Hello {model.name}</h1>;
            }
            """);

        var (_, renderer) = await PrepareAsync(project);
        var result = await RenderAsync(renderer, project, "Home/Index", new { name = "World" });

        result.Html.ShouldBe("""<h1 class="title">Hello World</h1>""");
    }

    [Fact]
    public async Task Context_PreactMode_IsSupported()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            import { createContext } from "preact";
            import { useContext } from "preact/hooks";

            const Currency = createContext("$");

            function Price({ amount }: { amount: number }) {
                return <span>{useContext(Currency)}{amount}</span>;
            }

            export default function Index() {
                return <Currency.Provider value="£"><Price amount={42} /></Currency.Provider>;
            }
            """);

        var (_, renderer) = await PrepareAsync(project);
        var result = await RenderAsync(renderer, project, "Home/Index");

        result.Html.ShouldBe("<span>£42</span>");
    }

    [Fact]
    public async Task Hooks_PreactMode_SupportsTheFullSet()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            import { useState, useMemo, useReducer } from "preact/hooks";

            export default function Index({ model }: { model: { items: number[] } }) {
                const [base] = useState(10);
                const total = useMemo(() => model.items.reduce((a, b) => a + b, 0), [model.items]);
                const [count] = useReducer((s: number) => s + 1, 5);
                return <output>{base + total + count}</output>;
            }
            """);

        var (_, renderer) = await PrepareAsync(project);
        var result = await RenderAsync(renderer, project, "Home/Index", new { items = new[] { 1, 2, 3 } });

        result.Html.ShouldBe("<output>21</output>");
    }

    [Fact]
    public async Task ReactImport_CompatibilityIsOn_ResolvesThroughPreactCompat()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            import { useState } from "react";
            export default function Index() {
                const [value] = useState("from react/compat");
                return <p>{value}</p>;
            }
            """);

        var (runtime, renderer) = await PrepareAsync(project);

        runtime.ResolveModule("react").ShouldBe("compat.js");

        var result = await RenderAsync(renderer, project, "Home/Index");
        result.Html.ShouldBe("<p>from react/compat</p>");
    }

    [Fact]
    public async Task ReactImport_CompatibilityIsOff_IsNotMapped()
    {
        using var project = PreactProject();
        project.Options.EnableReactCompatibility = false;

        var stager = StagerFor(project);
        stager.Stage();
        var runtime = JsxRuntimeLayout.Preact(stager, project.Options.EnableReactCompatibility);

        runtime.ResolveModule("react").ShouldBeNull();
        runtime.ResolveModule("preact").ShouldBe("preact.js");
    }

    [Fact]
    public async Task DotnetGlobal_PreactMode_IsStillCallable()
    {
        using var project = PreactProject();
        project.Options.Globals.Register("Greeter", new Greeter());
        project.AddView("Home/Index.tsx", """
            import { dotnet } from "dotnet:globals";
            export default function Index() {
                const greeter = dotnet.Greeter as { greet(name: string): string };
                return <p>{greeter.greet("Preact")}</p>;
            }
            """);

        var (_, renderer) = await PrepareAsync(project);
        var result = await RenderAsync(renderer, project, "Home/Index");

        result.Html.ShouldBe("<p>Hello, Preact!</p>");
    }

    [Fact]
    public async Task HeadExport_PreactMode_PopulatesTheDocumentAsUsual()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            export const head = (model: { name: string }) => ({ title: `Hi ${model.name}` });
            export default function Index() { return <p>body</p>; }
            """);

        var (_, renderer) = await PrepareAsync(project);
        var result = await RenderAsync(renderer, project, "Home/Index", new { name = "World" });

        result.Head!.Title.ShouldBe("Hi World");
    }

    [Fact]
    public async Task HostedPipeline_PreactHybridRender_ServesPreactAndRequestsHydration()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", """
            import { useState } from "preact/hooks";
            export default function Index({ model }: { model: { name: string } }) {
                const [greeting] = useState("Hello");
                return <h1>{greeting} {model.name}</h1>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project, options =>
        {
            options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        });

        var html = await host.GetStringAsync("/hybrid/Index");

        html.ShouldContain("<h1>Hello World</h1>");
        html.ShouldContain("@jsxcore/preact/client");
        html.ShouldContain("\"hydrate\":true");

        // Preact's own modules are served from the staged directory.
        html.ShouldContain("\"preact\":\"/_jsx/v");

        var prefix = html[html.IndexOf("/_jsx/v", StringComparison.Ordinal)..];
        prefix = prefix[..prefix.IndexOf('"')];
        prefix = prefix[..prefix.IndexOf("/preact/", StringComparison.Ordinal)];

        var preactModule = await host.Client.GetAsync($"{prefix}/preact/preact.js");
        preactModule.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preactModule.Content.ReadAsStringAsync()).ShouldContain("export");
    }

    [Fact]
    public async Task HostedPipeline_PreactClientRender_DoesNotRequestHydration()
    {
        using var project = PreactProject();
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");

        await using var host = await JsxTestHost.StartAsync(project, options =>
        {
            options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());
        });

        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("\"hydrate\":false");
        html.ShouldNotContain("<p>hi</p>");
    }

    [Fact]
    public void Registration_PreactIsNotInstalled_StartsAnyway()
    {
        // Preact ships inside JsxCore, so there is no "forgot npm install" case left: a directory
        // with no node_modules above it renders exactly like one that has them.
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-preact", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Views"));

        var options = new JsxCoreOptions { TypeScriptCompilerPath = JsxProjectFixture.Toolchain.ExecutablePath };

        try
        {
            Should.NotThrow(() => EnvironmentVerifier.Verify(options, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Greeter
    {
        public string Greet(string name) => $"Hello, {name}!";
    }
}
