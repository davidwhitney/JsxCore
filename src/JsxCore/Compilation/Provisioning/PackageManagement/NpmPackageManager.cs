using System.Diagnostics;
using System.Text;

namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed class NpmPackageManager(
    string? executablePath = null,
    TimeSpan? timeout = null,
    Action<string>? report = null) : IPackageManager
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(5);
    private readonly Action<string> _report = report ?? (_ => { });

    private string? _resolved;
    private bool _probed;

    public string Name => "npm";

    public string? ExecutablePath => Locate();

    // Probed once: the check launches a process and callers ask repeatedly.
    public bool IsAvailable() => Locate() is not null;

    private string? Locate()
    {
        if (_probed)
        {
            return _resolved;
        }

        _probed = true;
        _resolved = Find(executablePath);
        return _resolved;
    }

    public static string? Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        // On Windows npm is a shell script, so the executable is npm.cmd.
        // On Windows npm is a shell script, so the executable is npm.cmd.
        foreach (var candidate in OperatingSystem.IsWindows() ? ["npm.cmd", "npm"] : new[] { "npm" })
        {
            if (CanRun(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public PackageOperationResult CreateManifest(string directory)
    {
        _report($"JsxCore: no package.json found, creating one in {directory}.");
        return Run(directory, "created package.json", ["init", "-y"]);
    }

    public PackageOperationResult RestoreFromLockFile(string directory) =>
        Run(directory, "restored packages from the lock file", ["ci"]);

    public PackageOperationResult InstallDeclared(string directory) =>
        Run(directory, "installed declared packages", ["install"]);

    public PackageOperationResult Add(string directory, IReadOnlyCollection<PackageRequest> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        if (packages.Count == 0)
        {
            return PackageOperationResult.NothingToDo;
        }

        // Development and runtime packages are saved to different blocks, so they cannot be one call.
        foreach (var group in packages.GroupBy(package => package.Development))
        {
            var specifiers = group.Select(package => package.Specifier).ToList();
            _report($"JsxCore: installing {string.Join(", ", specifiers)}. This runs once.");

            string[] arguments = [.. new[] { "install", group.Key ? "--save-dev" : "--save" }, .. specifiers];
            var result = Run(directory, $"installed {string.Join(", ", specifiers)}", arguments);

            if (!result.Succeeded)
            {
                return result;
            }
        }

        return PackageOperationResult.Ok($"installed {packages.Count} package(s)");
    }

    private PackageOperationResult Run(string directory, string description, string[] arguments)
    {
        if (Locate() is not { } npm)
        {
            return PackageOperationResult.Failed(description, "npm was not found on PATH.");
        }

        var startInfo = StartInfoFor(npm, arguments);
        startInfo.WorkingDirectory = directory;

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new JsxCoreException($"JsxCore could not start '{npm}'.");

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return PackageOperationResult.Failed(
                    description, $"'npm {arguments[0]}' did not finish within {_timeout.TotalSeconds:0} seconds.");
            }

            return process.ExitCode == 0
                ? PackageOperationResult.Ok(description)
                : PackageOperationResult.Failed(description, Summarise(output.ToString()));
        }
        catch (Exception ex) when (ex is not JsxCoreException)
        {
            return PackageOperationResult.Failed(description, ex.Message);
        }
    }

    /// <summary>
    /// How to start npm, which on Windows is not a program.
    /// </summary>
    /// <remarks>
    /// npm ships there as npm.cmd, a batch script, and Windows cannot execute one directly:
    /// CreateProcess only runs real executables, so starting it throws and every attempt to find
    /// or run npm fails on a machine that plainly has it. It goes through the command interpreter
    /// instead, which is what a shell does when you type "npm" yourself.
    /// </remarks>
    public static ProcessStartInfo StartInfoFor(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!IsWindowsScript(executable))
        {
            startInfo.FileName = executable;

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

        // "/s /c" with the whole command wrapped in one pair of quotes is the documented way to
        // hand cmd a command line verbatim: it strips the outer pair and leaves the rest alone,
        // which is what keeps a path like "C:\Program Files\nodejs\npm.cmd" in one piece.
        var command = string.Join(' ', arguments.Prepend(executable).Select(Quote));
        startInfo.Arguments = $"/s /c \"{command}\"";

        return startInfo;
    }

    private static bool IsWindowsScript(string executable) =>
        OperatingSystem.IsWindows()
        && (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

    private static string Quote(string value) =>
        value.Length > 0 && !value.Contains(' ') ? value : $"\"{value}\"";

    private static bool CanRun(string executable)
    {
        try
        {
            using var process = Process.Start(StartInfoFor(executable, ["--version"]));
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(15_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Summarise(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "no output" : string.Join(" ", lines.TakeLast(4));
    }
}
