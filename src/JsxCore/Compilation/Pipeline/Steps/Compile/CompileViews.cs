namespace JsxCore.Compilation.Pipeline.Steps.Compile;

public sealed class CompileViews(Func<string, CancellationToken, Task> compile) : IBuildStep
{
    public string Name => "compile views";

    public bool AppliesTo(BuildContext context) => !context.Precompiled && context.Options.CompileOnStartup;

    // Compilation is handed in because the same work happens again on every file change,
    // without re-running the rest of the pipeline.
    public async ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        await compile(context.Fingerprint, cancellationToken).ConfigureAwait(false);
        return StepResult.None;
    }
}
