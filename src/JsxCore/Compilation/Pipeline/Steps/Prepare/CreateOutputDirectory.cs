namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

public sealed class CreateOutputDirectory : IBuildStep
{
    public string Name => "create output directory";

    public bool AppliesTo(BuildContext context) => !context.Precompiled;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.Layout.OutputDirectory);
        return new ValueTask<StepResult>(StepResult.None);
    }
}
