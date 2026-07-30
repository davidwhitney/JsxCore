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

    /// <param name="readRequired">
    /// Reads the source of a required module, by the specifier it was required with. Used to follow
    /// a re-export: see <see cref="ReExportedNames"/>.
    /// </param>
    public static string Wrap(
        string source,
        IReadOnlyDictionary<string, string> resolved,
        Func<string, string?>? readRequired = null)
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

        // A module's own exports, plus those of whatever it re-exports wholesale. The values are
        // read off module.exports at run time, so it does not matter which branch of the entry
        // actually ran: only the list of names has to be known while the module is being linked.
        var exported = FindNamedExports(source)
            .Concat(ReExportedNames(source, readRequired))
            .Distinct(StringComparer.Ordinal);

        foreach (var name in exported)
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

    /// <summary>
    /// Names exported by whatever <c>module.exports = require(...)</c> points at.
    /// </summary>
    /// <remarks>
    /// React's entries are the shape that forced this: a NODE_ENV branch between a development and
    /// a production build, assigning no named exports of their own. Both branches are followed and
    /// their names combined, which is safe because each resolves through <c>module.exports</c> at
    /// run time: a name only one branch defines is undefined, exactly as it would be in Node.
    /// </remarks>
    public static IEnumerable<string> ReExportedNames(string source, Func<string, string?>? readRequired)
    {
        if (readRequired is null)
        {
            yield break;
        }

        foreach (Match match in ReExportPattern().Matches(source))
        {
            if (readRequired(match.Groups["specifier"].Value) is not { } required)
            {
                continue;
            }

            foreach (var name in FindNamedExports(required))
            {
                yield return name;
            }
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex(@"(?<![\w$.])require\s*\(\s*(?<q>['""])(?<id>[^'""]+)\k<q>\s*\)")]
    private static partial Regex RequirePattern();

    [GeneratedRegex(@"(?<![\w$.])(?:module\.exports|exports)\s*\.\s*(?<name>[A-Za-z_$][\w$]*)\s*=(?!=)")]
    private static partial Regex NamedExportPattern();

    [GeneratedRegex(@"module\s*\.\s*exports\s*=\s*require\(\s*[""'](?<specifier>[^""']+)[""']\s*\)")]
    private static partial Regex ReExportPattern();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex IdentifierPattern();
}
