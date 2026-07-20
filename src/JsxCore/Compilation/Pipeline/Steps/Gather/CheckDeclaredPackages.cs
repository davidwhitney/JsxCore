using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline.Steps.Gather;

public sealed class CheckDeclaredPackages : IBuildStep
{
    public string Name => "check declared packages";

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (context.Inputs.MissingPackages is { Count: > 0 } missing)
        {
            context.Logger.LogWarning(
                "JsxCore: package.json declares {Packages}, which {Verb} not installed in {Directory}. " +
                "A view importing one will fail to render. Build again to restore them.",
                string.Join(", ", missing),
                missing.Count == 1 ? "is" : "are",
                context.Inputs.NodeModules.Roots.FirstOrDefault() ?? context.Layout.ContentRoot);
        }

        return new ValueTask<StepResult>(StepResult.None);
    }
}
