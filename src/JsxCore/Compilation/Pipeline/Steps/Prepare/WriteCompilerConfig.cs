namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

public sealed class WriteCompilerConfig : IBuildStep
{
    public string Name => "write compiler configuration";

    public bool AppliesTo(BuildContext context) => !context.Precompiled;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        TsConfigWriter.WriteIfChanged(context.Options, context.Layout, context.Inputs.NodeModules);

        // Only rewritten while it still carries JsxCore's marker, so a hand-edited one is left be.
        if (context.Options.GenerateEditorTsConfig)
        {
            TsConfigWriter.WriteIdeConfigIfOwned(context.Options, context.Layout);
        }

        return new ValueTask<StepResult>(StepResult.None);
    }
}
