using JsxCore.Compilation;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;

namespace JsxCore.Tool;

/// <summary>
/// Resolves the static assets and stylesheets views import, at build time.
/// </summary>
/// <remarks>
/// The view engine does this itself when it compiles at startup. This is the other deployment:
/// views compiled by the build and published as output, where nothing recompiles and whatever the
/// build left on disk is what the browser gets. Both call the same code, so a build-time
/// compilation and a startup compilation cannot disagree about what an import resolved to.
/// </remarks>
public static class AssetsCommand
{
    public static int Run(Arguments arguments)
    {
        var options = new JsxCoreOptions
        {
            ViewsDirectory = arguments.Required("views"),
            WorkingDirectory = arguments.Required("working")
        };

        if (arguments.Optional("web-root") is { } webRoot)
        {
            options.WebRootDirectory = webRoot;
        }

        var projectDirectory = arguments.Required("project-dir");
        var layout = CompilationLayout.Create(options, projectDirectory);

        // Before anything reads the output, for the same reason the view engine does it first: the
        // compiler never revisits what it emitted, so a deleted view leaves its JavaScript behind,
        // and publishing carries the whole directory. Without this a removed view keeps being
        // shipped, and a precompiled application still serves it by name.
        var pruned = CompiledOutput.PruneOrphans(layout, options.Extensions);

        if (pruned > 0)
        {
            Console.WriteLine($"JsxCore: removed {pruned} compiled module(s) whose view no longer exists.");
        }

        // Handed the same tools the view engine gets. Without them a stylesheet is left exactly as
        // the compiler wrote it, which a published application has no later opportunity to fix.
        var linked = ViewAssetLinker.Link(
            layout,
            EsbuildToolchainLocator.Locate(projectDirectory, arguments.Optional("esbuild")),
            new NodeModuleResolver(NodeModulesLayout.For(projectDirectory)));

        if (linked.Linked > 0)
        {
            Console.WriteLine($"JsxCore: resolved {linked.Linked} static asset import(s).");
        }

        // On stderr and without failing the build: a missing image is worth saying loudly and is
        // not worth refusing to produce an application over.
        foreach (var specifier in linked.Unresolved.Distinct(StringComparer.Ordinal))
        {
            Console.Error.WriteLine(
                $"JsxCore: could not resolve the asset import '{specifier}': there is no such " +
                $"file under {layout.WebRoot}.");
        }

        foreach (var specifier in linked.Misplaced.Distinct(StringComparer.Ordinal))
        {
            Console.Error.WriteLine(
                $"JsxCore: left the asset import '{specifier}' as written. Only a rooted path " +
                $"names something JsxCore serves, as in \"/images/logo.svg\".");
        }

        foreach (var diagnostic in linked.Diagnostics)
        {
            Console.Error.WriteLine("JsxCore: " + diagnostic.Message);
        }

        // The one thing here worth failing a build over. A tool that was found and then refused the
        // work has produced output that is wrong rather than incomplete, and publishing it would
        // mean shipping a page whose class names do not match its stylesheet. A tool that is simply
        // absent is a different matter, and leaves the exit code alone.
        return linked.Failed ? 1 : 0;
    }
}
