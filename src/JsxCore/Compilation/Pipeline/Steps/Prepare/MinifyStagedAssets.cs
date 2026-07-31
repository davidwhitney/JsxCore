using JsxCore.Compilation.Assets;

namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

/// <summary>
/// Minifies the framework files staged for the browser.
/// </summary>
/// <remarks>
/// Runs after the stagers, because it rewrites what they wrote. Compiled views are minified
/// elsewhere, between compiling and taking the build id; these are staged once per pipeline run
/// rather than on every file change, so they are done here instead of being redone on each rebuild.
/// </remarks>
public sealed class MinifyStagedAssets(JsMinifier? minifier, params string?[] directories) : IBuildStep
{
    public string Name => "minify staged assets";

    public bool AppliesTo(BuildContext context) => minifier is not null;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        foreach (var directory in directories.Where(directory => !string.IsNullOrEmpty(directory)))
        {
            minifier!.MinifyDirectory(directory!);
        }

        // The staged fingerprints are taken from the files as staged, before this ran, so without a
        // contribution here turning minification on would serve different bytes at an identical
        // URL — and those URLs are cached for a year.
        return new ValueTask<StepResult>(new StepResult($"min{minifier!.Version}"));
    }
}
