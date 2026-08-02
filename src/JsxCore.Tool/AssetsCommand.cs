using JsxCore.Compilation;
using JsxCore.Compilation.Assets;

namespace JsxCore.Tool;

/// <summary>
/// Resolves the static assets views import, at build time.
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

        var layout = CompilationLayout.Create(options, arguments.Required("project-dir"));
        var linked = ViewAssetLinker.Link(layout);

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

        return 0;
    }
}
