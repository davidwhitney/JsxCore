using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning;

public sealed record TypeScriptToolchain(string ExecutablePath, string Version, int MajorVersion, bool IsNative);

public static partial class TypeScriptToolchainLocator
{
    private const string PackageScope = "@typescript";

    public static string PlatformPackageName()
    {
        var os = PlatformName();
        var arch = ArchitectureName();
        return $"typescript-{os}-{arch}";
    }

    internal static string PlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win32";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) return "freebsd";
        return RuntimeInformation.OSDescription.ToLowerInvariant();
    }

    internal static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "ia32",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        var other => other.ToString().ToLowerInvariant()
    };
    
    public static IReadOnlyList<string> CandidatePaths(string contentRoot, IEnumerable<string>? additionalSearchPaths = null) =>
        CandidatePaths(NodeModulesLayout.For(contentRoot, additionalSearchPaths));

    public static IReadOnlyList<string> CandidatePaths(NodeModulesLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var executable = OperatingSystem.IsWindows() ? "tsc.exe" : "tsc";
        return layout.CandidatePaths($"{PackageScope}/{PlatformPackageName()}/lib/{executable}");
    }

    public static TypeScriptToolchain? Locate(
        string contentRoot,
        string? explicitPath = null,
        IEnumerable<string>? additionalSearchPaths = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Probe(explicitPath) : null;
        }

        foreach (var candidate in CandidatePaths(contentRoot, additionalSearchPaths))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var toolchain = Probe(candidate);
            if (toolchain is not null)
            {
                return toolchain;
            }
        }

        return null;
    }

    /// <summary>Runs <c>--version</c> against a candidate and parses the result.</summary>
    public static TypeScriptToolchain? Probe(string executablePath)
    {
        string output;
        try
        {
            output = RunVersion(executablePath);
        }
        catch (Exception)
        {
            // An unreadable or non-executable candidate is simply not a usable toolchain.
            return null;
        }

        var match = VersionPattern().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var version = match.Groups[1].Value;
        var major = int.Parse(match.Groups[2].Value);

        // The native compiler is a real executable; the legacy one is a Node shim script.
        var isNative = !LooksLikeScript(executablePath);

        return new TypeScriptToolchain(Path.GetFullPath(executablePath), version, major, isNative);
    }

    private static bool LooksLikeScript(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            return stream.Read(header) == 2 && header[0] == (byte)'#' && header[1] == (byte)'!';
        }
        catch
        {
            return false;
        }
    }

    private static string RunVersion(string executablePath)
    {
        var result = ToolProcess.Run(executablePath, ["--version"], timeout: TimeSpan.FromSeconds(15));

        return result.Outcome switch
        {
            ToolOutcome.TimedOut =>
                throw new TimeoutException($"'{executablePath} --version' did not complete within 15 seconds."),
            ToolOutcome.CouldNotStart =>
                throw new InvalidOperationException($"Could not start '{executablePath}'."),
            _ => result.Output
        };
    }

    [GeneratedRegex(@"(?:Version\s+)?((\d+)\.\d+\.\d+[^\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
