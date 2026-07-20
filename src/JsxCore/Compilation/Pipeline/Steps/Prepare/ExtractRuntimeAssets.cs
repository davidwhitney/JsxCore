using JsxCore.Compilation.Assets;
namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

public sealed class ExtractRuntimeAssets : IBuildStep
{
    public string Name => "extract runtime assets";

    public bool AppliesTo(BuildContext context) => !context.Precompiled;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken) =>
        new(new StepResult(AssetStage.WriteTo(context.Layout.RuntimeDirectory, new RuntimeTypeDefinitions()).Fingerprint));
}
