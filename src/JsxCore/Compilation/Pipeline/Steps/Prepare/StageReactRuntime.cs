using JsxCore.Compilation.Assets;

namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

public sealed class StageReactRuntime(ReactEntryStager? stager) : IBuildStep
{
    public string Name => "stage react";

    // Runs for a precompiled application too: these files are served to the browser rather than
    // compiled, so they have to be present whether or not anything is being built.
    public bool AppliesTo(BuildContext context) => stager is not null;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken) =>
        new(new StepResult(stager!.Stage()));
}
