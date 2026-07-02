using System.Text.RegularExpressions;

namespace JsxCore.Compilation;

public enum DiagnosticSeverity
{
    Message,
    Warning,
    Error
}

public sealed record CompilerDiagnostic(
    string? FilePath,
    int Line,
    int Column,
    DiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public override string ToString() =>
        FilePath is null
            ? $"{Severity.ToString().ToLowerInvariant()} {Code}: {Message}"
            : $"{FilePath}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

public static partial class DiagnosticParser
{
    public static IReadOnlyList<CompilerDiagnostic> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var diagnostics = new List<CompilerDiagnostic>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            // Continuation lines of a multi-line message are indented; append them to the previous one.
            if (char.IsWhiteSpace(line[0]) && diagnostics.Count > 0)
            {
                var previous = diagnostics[^1];
                diagnostics[^1] = previous with { Message = previous.Message + Environment.NewLine + line.Trim() };
                continue;
            }

            var located = LocatedPattern().Match(line);
            if (located.Success)
            {
                diagnostics.Add(new CompilerDiagnostic(
                    located.Groups["file"].Value,
                    int.Parse(located.Groups["line"].Value),
                    int.Parse(located.Groups["column"].Value),
                    ParseSeverity(located.Groups["severity"].Value),
                    located.Groups["code"].Value,
                    located.Groups["message"].Value.Trim()));
                continue;
            }

            var global = GlobalPattern().Match(line);
            if (global.Success)
            {
                diagnostics.Add(new CompilerDiagnostic(
                    null,
                    0,
                    0,
                    ParseSeverity(global.Groups["severity"].Value),
                    global.Groups["code"].Value,
                    global.Groups["message"].Value.Trim()));
            }
        }

        return diagnostics;
    }

    private static DiagnosticSeverity ParseSeverity(string value) => value.ToLowerInvariant() switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Message
    };

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning|message)\s+(?<code>TS\d+):\s*(?<message>.*)$")]
    private static partial Regex LocatedPattern();

    [GeneratedRegex(@"^(?<severity>error|warning|message)\s+(?<code>TS\d+):\s*(?<message>.*)$")]
    private static partial Regex GlobalPattern();
}
