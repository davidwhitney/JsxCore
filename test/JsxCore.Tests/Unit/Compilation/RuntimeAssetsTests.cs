using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Assets;

namespace JsxCore.Tests.Unit.Compilation;

public class RuntimeAssetsTests
{
    [Fact]
    public void RuntimeAssets_Queried_ContainEveryRuntimeModule()
    {
        foreach (var module in RuntimeAssets.PublicModules)
        {
            RuntimeAssets.TryGetText(module + ".js").ShouldNotBeNull($"{module}.js should be embedded");
        }

        RuntimeAssets.TryGetText("hmr-client.js").ShouldNotBeNull();
        RuntimeAssets.TryGetText("index.d.ts").ShouldNotBeNull();
        RuntimeAssets.TryGetText("head.js").ShouldNotBeNull();
    }

    [Fact]
    public void TryGetContent_FileIsNotEmbedded_ReturnsNull()
    {
        RuntimeAssets.TryGetContent("nope.js").ShouldBeNull();
        RuntimeAssets.TryGetContent("").ShouldBeNull();

        // A traversal attempt is simply not a known file name.
        RuntimeAssets.TryGetContent("../../secrets.txt").ShouldBeNull();
    }

    [Fact]
    public void ExtractTypeDefinitions_Runs_WritesOnlyDeclarationsToDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jsxcore-assets", Guid.NewGuid().ToString("n")[..8]);
        try
        {
            var hash = RuntimeAssets.ExtractTypeDefinitions(directory);

            hash.ShouldNotBeNullOrWhiteSpace();
            Directory.GetFiles(directory, "*.d.ts").ShouldNotBeEmpty();
            Directory.GetFiles(directory, "*.js").ShouldBeEmpty();

            // Stable across runs, so the build id does not churn on restart.
            RuntimeAssets.ExtractTypeDefinitions(directory).ShouldBe(hash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
