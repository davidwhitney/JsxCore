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
        var working = arguments.Required("working");

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
        var directory = Path.Combine(projectDirectory, working, "js");
        var count = minifier.MinifyDirectory(directory);

        if (count > 0)
        {
            Console.WriteLine($"JsxCore: minified {count} view(s) with esbuild {toolchain.Version}.");
        }

        return Done;
    }
}
