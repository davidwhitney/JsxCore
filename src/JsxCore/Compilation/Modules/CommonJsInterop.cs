using System.Text;
using System.Text.RegularExpressions;

namespace JsxCore.Compilation.Modules;

public static partial class CommonJsInterop
{
    public sealed record RequiredModule(string Specifier, string Variable);

    public static IReadOnlyList<string> FindRequires(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var seen = new List<string>();
        foreach (Match match in RequirePattern().Matches(source))
        {
            var specifier = match.Groups["id"].Value;
            if (specifier.Length > 0 && !seen.Contains(specifier, StringComparer.Ordinal))
            {
                seen.Add(specifier);
            }
        }
        return seen;
    }

    public static IReadOnlyList<string> FindNamedExports(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var names = new List<string>();
        foreach (Match match in NamedExportPattern().Matches(source))
        {
            var name = match.Groups["name"].Value;
            if (name is "default" or "module" or "exports" || names.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }
            names.Add(name);
        }
        return names;
    }

    public static string Wrap(string source, IReadOnlyDictionary<string, string> resolved)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolved);

        var builder = new StringBuilder();
        builder.AppendLine("// Wrapped from CommonJS by JsxCore.");

        var bindings = new List<(string Specifier, string Variable)>();
        var index = 0;
        foreach (var (specifier, target) in resolved)
        {
            var variable = $"__jsxcore_cjs_{index++}";
            bindings.Add((specifier, variable));
            builder.Append("import ").Append(variable).Append(" from ")
                .Append(Quote(target)).AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine("const __jsxcore_deps = new Map([");
        foreach (var (specifier, variable) in bindings)
        {
            builder.Append("    [").Append(Quote(specifier)).Append(", ").Append(variable).AppendLine("],");
        }
        builder.AppendLine("]);");
        builder.AppendLine();
        builder.AppendLine("const module = { exports: {} };");
        builder.AppendLine("let exports = module.exports;");
        builder.AppendLine("function require(id) {");
        builder.AppendLine("    if (__jsxcore_deps.has(id)) { return __jsxcore_deps.get(id); }");
        builder.AppendLine("    throw new Error(");
        builder.AppendLine("        \"JsxCore: require('\" + id + \"') could not be resolved during server rendering. \" +");
        builder.AppendLine("        \"Node built-in modules are not available in the embedded engine.\");");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("(function (module, exports, require) {");
        builder.AppendLine(source);
        builder.AppendLine("})(module, exports, require);");
        builder.AppendLine();
        builder.AppendLine("export default module.exports;");

        foreach (var name in FindNamedExports(source))
        {
            // Valid identifiers only: anything else is still reachable through the default export.
            if (IdentifierPattern().IsMatch(name))
            {
                builder.Append("export const ").Append(name).Append(" = module.exports?.")
                    .Append(name).AppendLine(";");
            }
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex(@"(?<![\w$.])require\s*\(\s*(?<q>['""])(?<id>[^'""]+)\k<q>\s*\)")]
    private static partial Regex RequirePattern();

    [GeneratedRegex(@"(?<![\w$.])(?:module\.exports|exports)\s*\.\s*(?<name>[A-Za-z_$][\w$]*)\s*=(?!=)")]
    private static partial Regex NamedExportPattern();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex IdentifierPattern();
}
