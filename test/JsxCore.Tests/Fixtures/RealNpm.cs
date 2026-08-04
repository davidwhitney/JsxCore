using System.Diagnostics;
using JsxCore.Compilation.Provisioning.PackageManagement;

namespace JsxCore.Tests.Fixtures;

/// <summary>
/// Whether npm is on this machine, worked out independently of the code that answers the same
/// question.
/// </summary>
/// <remarks>
/// The suite no longer needs npm for anything: it restores its own packages with JsxCore's native
/// client. So the tests covering the npm strategy have to assert what is true here rather than
/// assume npm exists, and a probe that shared an implementation with the thing under test would
/// agree with it whether or not either was right.
/// </remarks>
public static class RealNpm
{
    public static bool IsInstalled { get; } = FindOnPath() is not null;

    /// <summary>
    /// Runs real npm and returns what it said, for the tests that check JsxCore against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Started through <see cref="NpmPackageManager.StartInfoFor"/> rather than directly, because
    /// npm on Windows is a batch script that cannot be launched as a program. Sharing that with
    /// the code under test is deliberate: two ways of starting npm would be one too many.
    /// </para>
    /// <para>
    /// Both streams are drained while the process runs. Reading one to the end first deadlocks the
    /// moment npm fills the other pipe's buffer: npm blocks writing, the stream being read never
    /// reaches its end, and the read has no timeout to escape through. It hangs until something
    /// kills the build, which is how this was found.
    /// </para>
    /// </remarks>
    public static (int ExitCode, string Output) Run(string workingDirectory, TimeSpan timeout, params string[] arguments)
    {
        var npm = NpmPackageManager.Find()
                  ?? throw new InvalidOperationException("npm is not installed, so this should not have been called.");

        var startInfo = NpmPackageManager.StartInfoFor(npm, arguments);
        startInfo.WorkingDirectory = workingDirectory;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start '{npm}'.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, $"npm {string.Join(' ', arguments)} did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        // The overload without a timeout is what waits for the redirected streams to close.
        process.WaitForExit();

        return (process.ExitCode, error.Result + output.Result);
    }

    private static string? FindOnPath()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "npm.cmd", "npm.exe", "npm" } : ["npm"];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory.Trim(), name);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not worth failing over.
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
