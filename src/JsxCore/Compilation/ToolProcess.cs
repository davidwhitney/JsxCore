using System.Diagnostics;

namespace JsxCore.Compilation;

/// <summary>How running a tool ended.</summary>
public enum ToolOutcome
{
    /// <summary>The process ran to completion. <see cref="ToolResult.ExitCode"/> says how it went.</summary>
    Exited,

    /// <summary>It was still running when the timeout expired, and has been killed.</summary>
    TimedOut,

    /// <summary>It never started: a missing file, or one the operating system refused to execute.</summary>
    CouldNotStart
}

/// <summary>What a tool did, and whatever it said while doing it.</summary>
public sealed record ToolResult(
    ToolOutcome Outcome,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => Outcome is ToolOutcome.Exited && ExitCode == 0;

    /// <summary>
    /// Both streams, for the callers that only want to know what went wrong. Tools disagree about
    /// which one a diagnostic belongs on, and none of them is worth reading separately when the
    /// answer is going into a log line.
    /// </summary>
    public string Output => string.IsNullOrEmpty(StandardError)
        ? StandardOutput
        : string.IsNullOrEmpty(StandardOutput)
            ? StandardError
            : StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// Runs one of the native binaries JsxCore drives: the TypeScript compiler, esbuild, npm.
/// </summary>
/// <remarks>
/// <para>
/// There is one of these because getting it right is fiddly and getting it wrong is invisible.
/// Both pipes have to be drained while the process runs, or a tool that writes enough to fill one
/// blocks forever waiting for a reader that is itself blocked on the other. A timeout has to be
/// applied to the wait rather than after a blocking read, or it can never fire. And a process that
/// outlives its timeout has to be killed, or the build leaks it.
/// </para>
/// <para>
/// Every caller degrades rather than throwing, so this reports failure in the result instead of
/// raising: a tool that will not start is a thing to explain, and the callers all have a better
/// message for it than an exception would carry.
/// </para>
/// </remarks>
public static class ToolProcess
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long to keep reading after the process has gone, before giving up on the tail.</summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    public static ToolResult Run(
        string executablePath,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null) =>
        Run(StartInfoFor(executablePath, arguments, workingDirectory), timeout);

    /// <summary>
    /// Runs a command that has already been described, for the callers that build their own.
    /// </summary>
    /// <remarks>
    /// npm on Windows is a <c>.cmd</c> shim, which has to be invoked through the command
    /// interpreter with a hand-quoted command line rather than an argument list. That is the only
    /// reason this overload exists: the redirection, waiting and killing are the same either way.
    /// </remarks>
    public static ToolResult Run(ProcessStartInfo startInfo, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        Redirect(startInfo);

        var process = TryStart(startInfo, out var failure);
        if (process is null)
        {
            return failure!;
        }

        using (process)
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)(timeout ?? DefaultTimeout).TotalMilliseconds))
            {
                Kill(process);
                return new ToolResult(ToolOutcome.TimedOut, -1, Drain(standardOutput), Drain(standardError));
            }

            // Exiting closes both pipes, which is what completes the reads above.
            Task.WaitAll([standardOutput, standardError], DrainGrace);

            return new ToolResult(
                ToolOutcome.Exited, process.ExitCode, Drain(standardOutput), Drain(standardError));
        }
    }

    /// <param name="cancellationToken">
    /// Cancellation kills the process and throws, which is what a request or a watch rebuild being
    /// abandoned should do. A timeout does not throw: it is a fact about the tool, not the caller.
    /// </param>
    public static async Task<ToolResult> RunAsync(
        string executablePath,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var process = TryStart(
            StartInfoFor(executablePath, arguments, workingDirectory), out var failure);

        if (process is null)
        {
            return failure!;
        }

        using (process)
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout ?? DefaultTimeout);

            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new ToolResult(ToolOutcome.TimedOut, -1, Drain(standardOutput), Drain(standardError));
            }

            return new ToolResult(
                ToolOutcome.Exited, process.ExitCode, Drain(standardOutput), Drain(standardError));
        }
    }

    private static ProcessStartInfo StartInfoFor(
        string executablePath, IEnumerable<string> arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executablePath);

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        // Never a single command line: a package path can contain spaces, and quoting them by hand
        // is the sort of thing that works everywhere except the one machine that matters.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Redirect(startInfo);
        return startInfo;
    }

    private static void Redirect(ProcessStartInfo startInfo)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
    }

    private static Process? TryStart(ProcessStartInfo startInfo, out ToolResult? failure)
    {
        try
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            if (process.Start())
            {
                failure = null;
                return process;
            }

            process.Dispose();
            failure = new ToolResult(ToolOutcome.CouldNotStart, -1, string.Empty,
                $"'{startInfo.FileName}' did not start.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception
                                              or InvalidOperationException or ObjectDisposedException)
        {
            failure = new ToolResult(ToolOutcome.CouldNotStart, -1, string.Empty, exception.Message);
            return null;
        }
    }

    /// <summary>Whatever a stream produced, or nothing when it never finished.</summary>
    private static string Drain(Task<string> read)
    {
        try
        {
            return read.Wait(DrainGrace) ? read.Result : string.Empty;
        }
        catch (AggregateException)
        {
            // A pipe torn down under a killed process, which costs the tail of a message we are
            // already reporting as a failure.
            return string.Empty;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
                                              or System.ComponentModel.Win32Exception)
        {
            // It exited between the wait giving up and this, which is the outcome we wanted.
        }
    }
}
