using System.IO.Compression;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Assets;

/// <summary>
/// Minified and compressed assets, served through the real pipeline.
/// </summary>
/// <remarks>
/// The minification tests need esbuild, which arrives with a Release build, and skip themselves
/// when it is absent rather than failing a machine that has only ever built Debug. Compression
/// needs nothing, so it is always covered.
/// </remarks>
public class MinificationTests
{
    private static EsbuildToolchain? Esbuild() =>
        EsbuildToolchainLocator.Locate(JsxProjectFixture.RepositoryRoot())
        ?? EsbuildToolchainLocator.Locate(
            Path.Combine(JsxProjectFixture.RepositoryRoot(), "samples", "SampleApp.React"));

    [Fact]
    public async Task Views_MinificationIsOn_AreServedMinified()
    {
        if (Esbuild() is null)
        {
            return;
        }

        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.Minify = true;
        project.Options.MinifierPath = Esbuild()!.ExecutablePath;
        project.AddView("Home/Index.tsx", """
            export default function Index({ model }: { model: { name: string } }) {
                const greeting = "hello " + model.name;
                return <h1>{greeting}</h1>;
            }
            """);

        await project.CompileAsync();

        var emitted = await File.ReadAllTextAsync(
            Path.Combine(project.Compilation.Layout.OutputDirectory, "Home", "Index.js"));

        // The local disappears into the expression that used it, which no amount of whitespace
        // stripping would do: this is a real minifier and not a formatter.
        emitted.ShouldNotContain("greeting");
        emitted.Trim().ShouldNotContain("\n    ");
    }

    [Fact]
    public async Task BuildId_MinificationIsTurnedOn_Changes()
    {
        if (Esbuild() is null)
        {
            return;
        }

        using var plain = JsxProjectFixture.Create();
        plain.Options.TypeChecking = TypeCheckingMode.Off;
        plain.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");
        await plain.CompileAsync();

        using var minified = JsxProjectFixture.Create();
        minified.Options.TypeChecking = TypeCheckingMode.Off;
        minified.Options.Minify = true;
        minified.Options.MinifierPath = Esbuild()!.ExecutablePath;
        minified.AddView("Home/Index.tsx", "export default function Index() { return <p>hi</p>; }");
        await minified.CompileAsync();

        // Asset URLs carry the build id and are cached for a year, so the same view minified and
        // unminified must not share one.
        minified.Compilation.BuildId.ShouldNotBe(plain.Compilation.BuildId);
    }

    [Fact]
    public async Task Assets_CompressionIsOn_AreServedCompressedAndVary()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.CompressAssets = true;
        project.AddView("Home/Index.tsx", """
            export default function Index() { return <p>a page with some content in it</p>; }
            """);

        await project.CompileAsync();
        await using var host = await JsxTestHost.StartAsync(project, options => options.CompressAssets = true);

        var url = $"/_jsx/v{project.Compilation.BuildId}/views/Home/Index.js";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        var response = await host.Client.SendAsync(request);

        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Content.Headers.ContentEncoding.ShouldContain("gzip");
        response.Headers.Vary.ShouldContain("Accept-Encoding");

        // The body has to survive the round trip, which is the part that matters.
        await using var body = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        (await reader.ReadToEndAsync()).ShouldContain("a page with some content in it");
    }

    [Fact]
    public async Task Assets_ClientTakesNoEncoding_AreServedAsIs()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>plain</p>; }");

        await project.CompileAsync();
        await using var host = await JsxTestHost.StartAsync(project, options => options.CompressAssets = true);

        var url = $"/_jsx/v{project.Compilation.BuildId}/views/Home/Index.js";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        var response = await host.Client.SendAsync(request);

        response.Content.Headers.ContentEncoding.ShouldBeEmpty();

        // Still varies: a shared cache holding this must not hand it to a client that wanted gzip.
        response.Headers.Vary.ShouldContain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync()).ShouldContain("plain");
    }

    [Fact]
    public async Task Assets_CompressionIsOff_AreServedAsIsWithNoVary()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.AddView("Home/Index.tsx", "export default function Index() { return <p>plain</p>; }");

        await project.CompileAsync();
        await using var host = await JsxTestHost.StartAsync(project, options => options.CompressAssets = false);

        var url = $"/_jsx/v{project.Compilation.BuildId}/views/Home/Index.js";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");

        var response = await host.Client.SendAsync(request);

        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        response.Headers.Vary.ShouldBeEmpty();
    }

    [Fact]
    public void Locate_EsbuildIsNotInstalled_IsNullRatherThanThrowing() =>
        EsbuildToolchainLocator.Locate(Path.GetTempPath(), "/definitely/not/esbuild").ShouldBeNull();

    [Fact]
    public void PlatformPackageName_OnThisMachine_NamesAPackageEsbuildPublishes()
    {
        // esbuild names its platform packages the same way TypeScript does, which is what lets the
        // same os/arch pair serve both.
        var name = EsbuildToolchainLocator.PlatformPackageName();

        name.ShouldContain("-");
        name.Split('-').Length.ShouldBe(2);
    }

    [Fact]
    public void CandidatePaths_OnThisMachine_LookUnderTheScopedPlatformPackage()
    {
        var paths = EsbuildToolchainLocator.CandidatePaths(
            NodeModulesLayout.For(JsxProjectFixture.RepositoryRoot()));

        paths.ShouldNotBeEmpty();
        paths.ShouldAllBe(path => path.Contains("@esbuild"));
    }
}
