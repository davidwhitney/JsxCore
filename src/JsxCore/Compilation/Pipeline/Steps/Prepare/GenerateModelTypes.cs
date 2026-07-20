using JsxCore.TypeScript;
using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

public sealed class GenerateModelTypes : IBuildStep
{
    public string Name => "generate model types";

    public bool AppliesTo(BuildContext context) => !context.Precompiled && context.Options.TypeDefinitions.Enabled;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var definitions = context.Options.TypeDefinitions;

        try
        {
            var generated = new TypeScriptDefinitionGenerator(definitions, context.Options.JsonSerializerOptions).Generate();
            var staged = generated.WriteTo(context.Layout.GeneratedTypesDirectory);

            if (staged.Changed)
            {
                context.Logger.LogInformation(
                    "JsxCore generated TypeScript declarations for {Count} .NET type(s) in {Path}.",
                    definitions.ResolveTypes().Count, context.Layout.GeneratedTypesDirectory);
            }

            return new ValueTask<StepResult>(new StepResult(staged.Fingerprint));
        }
        catch (Exception ex) when (ex is not JsxCoreException)
        {
            // A model JsxCore cannot describe should not stop the application from serving views.
            context.Logger.LogError(ex, "JsxCore could not generate TypeScript declarations from .NET types.");
            return new ValueTask<StepResult>(StepResult.None);
        }
    }
}
