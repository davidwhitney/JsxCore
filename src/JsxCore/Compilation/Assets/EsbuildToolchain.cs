using System.Diagnostics;
using System.Text.RegularExpressions;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Compilation.Assets;

public sealed record EsbuildToolchain(string ExecutablePath, string Version);

/// <summary>
/// Finds the esbuild binary, which minifies what is served to the browser.
/// </summary>
/// <remarks>
/// The same arrangement as the TypeScript compiler: a native executable published to npm as one
/// package per platform, restored by JsxCore and run as a process. Nothing about it needs Node, so
/// minification costs a binary on the build machine and changes nothing else about the design.
/// </remarks>
public static partial class EsbuildToolchainLocator
{
    public const string PackageName = "esbuild";
    private const string PackageScope = "@esbuild";

    /// <summary>The platform package for this machine, as <c>darwin-arm64</c>.</summary>
    public static string PlatformPackageName() =>
        $"{TypeScriptToolchainLocator.PlatformName()}-{TypeScriptToolchainLocator.ArchitectureName()}";

    public static IReadOnlyList<string> CandidatePaths(NodeModulesLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var package = $"{PackageScope}/{PlatformPackageName()}";

        // Windows puts the executable at the package root; everywhere else it is under bin/.
        return OperatingSystem.IsWindows()
            ? layout.CandidatePaths($"{package}/esbuild.exe")
            : layout.CandidatePaths($"{package}/bin/esbuild");
    }

    public static EsbuildToolchain? Locate(
        string contentRoot,
        string? explicitPath = null,
        IEnumerable<string>? additionalSearchPaths = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Probe(explicitPath) : null;
        }

        return CandidatePaths(NodeModulesLayout.For(contentRoot, additionalSearchPaths))
            .Where(File.Exists)
            .Select(Probe)
            .FirstOrDefault(toolchain => toolchain is not null);
    }

    /// <summary>
    /// Runs the binary to confirm it is the thing it appears to be, rather than trusting the path.
    /// </summary>
    private static EsbuildToolchain? Probe(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);

            var match = VersionPattern().Match(output);
            return match.Success ? new EsbuildToolchain(path, match.Value) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}
