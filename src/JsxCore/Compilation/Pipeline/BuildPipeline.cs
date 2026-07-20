using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline;

public sealed class BuildPipeline(params IBuildStep[] steps)
{
    public IReadOnlyList<IBuildStep> Steps { get; } = steps ?? throw new ArgumentNullException(nameof(steps));

    public async ValueTask<string> RunAsync(BuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Order is load bearing: the compiler cannot resolve the runtime types until they are
        // on disk, nor a model type until it has been generated.
        foreach (var step in Steps.Where(step => step.AppliesTo(context)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await step.RunAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.Fingerprint is { } contribution)
            {
                context.Contribute(contribution);
            }

            context.Logger.LogDebug("JsxCore build step {Step} completed.", step.Name);
        }

        return context.Fingerprint;
    }
}
