using System.Diagnostics;
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

        var stopwatch = Stopwatch.StartNew();

        // No timeout: a cold compile of a large view tree is allowed to take as long as it takes,
        // and the caller's token is what stops it.
        var output = await ToolProcess.RunAsync(
            Toolchain.ExecutablePath,
            ["--project", layout.TsConfigPath, "--pretty", "false"],
            layout.WorkingDirectory,
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        if (output.Outcome == ToolOutcome.CouldNotStart)
        {
            throw new JsxCoreException(
                $"JsxCore could not start the TypeScript compiler at '{Toolchain.ExecutablePath}': " +
                output.StandardError);
        }

        var diagnostics = DiagnosticParser.Parse(output.Output);

        // A non-zero exit with nothing parseable means the compiler itself failed to run. Without
        // this, an unreadable tsconfig or a missing binary would look like a clean compilation.
        if (output.ExitCode != 0 && diagnostics.Count == 0)
        {
            return new CompilationResult(
                false,
                [new CompilerDiagnostic(null, 0, 0, DiagnosticSeverity.Error, "TS0000",
                    $"The TypeScript compiler exited with code {output.ExitCode}. Output:{Environment.NewLine}{output.Output}")],
                stopwatch.Elapsed,
                output.Output);
        }

        var succeeded = diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
        return new CompilationResult(succeeded, diagnostics, stopwatch.Elapsed, output.Output);
    }

}
