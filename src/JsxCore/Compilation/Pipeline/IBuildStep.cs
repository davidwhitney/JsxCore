namespace JsxCore.Compilation.Pipeline;

public interface IBuildStep
{
    string Name { get; }

    bool AppliesTo(BuildContext context) => true;

    ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken);
}
