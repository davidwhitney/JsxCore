using JsxCore.Compilation.Assets;
using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline.Steps.Emit;

/// <summary>
/// Minifies the compiled views, in place.
/// </summary>
/// <remarks>
/// After linking and before the build id is taken, so the id covers what is actually served rather
/// than what the compiler emitted. Only on success: minifying broken output helps nobody.
/// </remarks>
public sealed class MinifyCompiledViews(JsMinifier? minifier) : IBuildStep
{
    public string Name => "minify compiled views";

    public bool AppliesTo(BuildContext context) =>
        minifier is not null && context.Compilation?.Succeeded == true;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var count = minifier!.MinifyDirectory(context.Layout.OutputDirectory);

        if (count > 0)
        {
            context.Logger.LogInformation(
                "JsxCore minified {Count} compiled view(s) with esbuild {Version}.", count, minifier.Version);
        }

        return new ValueTask<StepResult>(StepResult.None);
    }
}
