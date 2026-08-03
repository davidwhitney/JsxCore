using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;
using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Pipeline.Steps.Emit;

/// <summary>
/// Turns the asset and stylesheet imports the compiler left alone into something a browser and the
/// server-side engine can both load, and records what each module brings with it.
/// </summary>
/// <remarks>
/// Runs whatever the compiler made of the views. A type error does not stop it emitting, and a page
/// that renders with a broken image is the worse outcome.
/// </remarks>
public sealed class LinkAssets(EsbuildToolchain? esbuild, NodeModuleResolver? npm) : IBuildStep
{
    public string Name => "link assets";

    public ValueTask<StepResult> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var linked = ViewAssetLinker.Link(context.Layout, esbuild, npm);

        context.Linked(linked.Manifest);

        // A tool that is absent leaves a feature unavailable; one that refused the work leaves the
        // output wrong. Logged apart so the second is not read as the first. Neither stops the
        // application: a running server that cannot scope a class name is still worth more than no
        // server, and the build says so loudly enough to be fixed before it ships.
        foreach (var diagnostic in linked.Diagnostics)
        {
            if (diagnostic.Problem == AssetProblem.Failed)
            {
                context.Logger.LogError("{Message}", diagnostic.Message);
            }
            else
            {
                context.Logger.LogWarning("{Message}", diagnostic.Message);
            }
        }

        if (linked.Linked > 0)
        {
            context.Logger.LogDebug("JsxCore linked {Count} static asset import(s).", linked.Linked);
        }

        // Reported rather than guessed at. Nothing else will say so, because the compiler has no
        // opinion about a scheme it does not know, and it would otherwise surface much later as a
        // module the browser cannot load.
        foreach (var specifier in linked.Unresolved.Distinct(StringComparer.Ordinal))
        {
            context.Logger.LogWarning(
                "JsxCore could not resolve the asset import '{Specifier}': there is no such file " +
                "under {WebRoot}. The path is the URL, so it starts at your web root.",
                specifier, context.Layout.WebRoot);
        }

        // Type checks, resolves to nothing. The ambient declarations cannot tell a rooted specifier
        // from a relative one, so this is the only place the difference can be reported.
        foreach (var specifier in linked.Misplaced.Distinct(StringComparer.Ordinal))
        {
            context.Logger.LogWarning(
                "JsxCore left the asset import '{Specifier}' as written, because only a rooted " +
                "path names something it serves. Put the file under {WebRoot} and import it as the " +
                "URL it is served from, as in \"/images/logo.svg\".", specifier, context.Layout.WebRoot);
        }

        return new ValueTask<StepResult>(StepResult.None);
    }
}
