using JsxCore.Compilation.Assets;

namespace JsxCore.Compilation.Pipeline.Steps.Prepare;

/// <summary>
/// Writes the JavaScript behind <c>dotnet:globals</c> into the runtime directory.
/// </summary>
/// <remarks>
/// Runs for a precompiled application too. The module is served to the browser and loaded by the
/// server renderer rather than compiled, so it has to exist whether or not anything is being built.
/// </remarks>
public sealed class WriteGlobalsModule : IBuildStep
{
    public string Name => "write globals module";

    public bool AppliesTo(BuildContext context) => true;

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var names = context.Options.Globals.Registrations.Keys;
        var path = Path.Combine(context.Layout.RuntimeDirectory, GeneratedGlobalsModule.FileName);

        Directory.CreateDirectory(context.Layout.RuntimeDirectory);
        AssetStage.WriteFileIfChanged(path, GeneratedGlobalsModule.For(names));

        // What is registered changes what is served, so it belongs in the build id.
        return new ValueTask<StepResult>(new StepResult(string.Join(",", names.OrderBy(n => n, StringComparer.Ordinal))));
    }
}
