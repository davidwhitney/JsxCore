using JsxCore.Compilation.Modules;
namespace JsxCore.Compilation.Pipeline.Steps.Gather;

public sealed class GatherProjectInputs : IBuildStep
{
    public string Name => "gather project inputs";

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var nodeModules = NodeModulesLayout.For(
            context.Layout.ContentRoot, context.Options.AdditionalToolchainSearchPaths);

        context.Gathered(new ProjectInputs(nodeModules, PackageManifest.Nearest(context.Layout.ContentRoot)));

        // Nothing is produced, so nothing is contributed: the build id describes output, not inputs.
        return new ValueTask<StepResult>(StepResult.None);
    }
}
