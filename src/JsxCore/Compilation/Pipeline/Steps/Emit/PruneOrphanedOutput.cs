using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline.Steps.Emit;

/// <summary>
/// Deletes compiled modules whose view no longer exists, before anything reads the output.
/// </summary>
/// <remarks>
/// First in the emit pipeline, so linking and minification do not spend work on modules that are
/// about to go, and so the manifest describes what is there rather than what used to be.
/// </remarks>
public sealed class PruneOrphanedOutput : IBuildStep
{
    public string Name => "prune orphaned output";

    // A precompiled application has no views directory to compare against, and every compiled
    // module would look orphaned.
    public bool AppliesTo(BuildContext context) => !context.Precompiled;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var removed = CompiledOutput.PruneOrphans(context.Layout, context.Options.Extensions);

        if (removed > 0)
        {
            context.Logger.LogInformation(
                "JsxCore removed {Count} compiled module(s) whose view no longer exists.", removed);
        }

        return new ValueTask<StepResult>(StepResult.None);
    }
}
