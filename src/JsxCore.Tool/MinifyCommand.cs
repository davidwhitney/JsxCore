using JsxCore.Compilation.Assets;
using Microsoft.Extensions.Logging.Abstractions;

namespace JsxCore.Tool;

/// <summary>
/// Minifies compiled views during the build.
/// </summary>
/// <remarks>
/// The view engine minifies what it compiles at startup, which covers an application that compiles
/// then. It does not cover the other deployment: views compiled by the build and published as
/// output, where nothing recompiles and whatever the build left on disk is what the browser gets.
/// This is that half.
/// </remarks>
public static class MinifyCommand
{
    public const int Done = 0;
    public const int NoMinifier = 0;

    public static int Run(Arguments arguments)
    {
        var projectDirectory = arguments.Required("project-dir");

        // A directory names itself: used at publish time for the packages copied into the output,
        // which are minified where they land rather than in anyone's node_modules.
        var directory = arguments.Optional("directory");
        var working = directory is null ? arguments.Required("working") : null;

        var toolchain = EsbuildToolchainLocator.Locate(projectDirectory, arguments.Optional("esbuild"));
        if (toolchain is null)
        {
            // Not an error. Minification is an optimisation, and a build that cannot do it should
            // produce a working application rather than no application.
            Console.Error.WriteLine(
                "JsxCore: esbuild was not found, so compiled views are not minified.");

            return NoMinifier;
        }

        var minifier = new JsMinifier(toolchain, NullLogger.Instance);
        var target = directory ?? Path.Combine(projectDirectory, working!, "js");
        var count = minifier.MinifyDirectory(target, preserveFormat: directory is not null);

        if (count > 0)
        {
            var what = directory is null ? "view" : "package file";
            Console.WriteLine($"JsxCore: minified {count} {what}(s) with esbuild {toolchain.Version}.");
        }

        return Done;
    }
}
