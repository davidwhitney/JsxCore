using System.Diagnostics;
using System.Text;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Compilation;

public sealed record CompilationResult(
    bool Succeeded,
    IReadOnlyList<CompilerDiagnostic> Diagnostics,
    TimeSpan Duration,
    string RawOutput)
{
    public IReadOnlyList<CompilerDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    public string FormatDiagnostics() => string.Join(Environment.NewLine, Diagnostics.Select(d => d.ToString()));
}

public sealed class TypeScriptCompiler(TypeScriptToolchain toolchain)
{
    public TypeScriptToolchain Toolchain { get; } = toolchain ?? throw new ArgumentNullException(nameof(toolchain));

    public async Task<CompilationResult> CompileAsync(CompilationLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var startInfo = new ProcessStartInfo(Toolchain.ExecutablePath)
        {
            WorkingDirectory = layout.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(layout.TsConfigPath);
        startInfo.ArgumentList.Add("--pretty");
        startInfo.ArgumentList.Add("false");

        var stopwatch = Stopwatch.StartNew();
        var output = await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var diagnostics = DiagnosticParser.Parse(output.Text);

        // A non-zero exit with nothing parseable means the compiler itself failed to run. Without
        // this, an unreadable tsconfig or a missing binary would look like a clean compilation.
        if (output.ExitCode != 0 && diagnostics.Count == 0)
        {
            return new CompilationResult(
                false,
                [new CompilerDiagnostic(null, 0, 0, DiagnosticSeverity.Error, "TS0000",
                    $"The TypeScript compiler exited with code {output.ExitCode}. Output:{Environment.NewLine}{output.Text}")],
                stopwatch.Elapsed,
                output.Text);
        }

        var succeeded = diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
        return new CompilationResult(succeeded, diagnostics, stopwatch.Elapsed, output.Text);
    }

    private static async Task<(string Text, int ExitCode)> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var buffer = new StringBuilder();
        var bufferLock = new object();

        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (bufferLock)
            {
                buffer.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new JsxCoreException(
                $"JsxCore could not start the TypeScript compiler at '{startInfo.FileName}'.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        lock (bufferLock)
        {
            return (buffer.ToString(), process.ExitCode);
        }
    }
}
