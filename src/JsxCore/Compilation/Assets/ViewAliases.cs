using System.Text.Json;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// The <c>paths</c> aliases that point into the views directory, read back from the compiler
/// configuration.
/// </summary>
/// <remarks>
/// <para>
/// TypeScript resolves <c>@/Shared/Card.tsx</c> through <c>paths</c> and then emits it exactly as
/// written: <c>rewriteRelativeImportExtensions</c> rewrites relative specifiers, and an aliased one
/// is not relative. So the specifier reaches the browser still aliased and still carrying
/// <c>.tsx</c>, and neither the browser nor the server module loader can do anything with it.
/// </para>
/// <para>
/// Read from the generated configuration rather than from options, so an alias the application
/// added itself is rewritten on the same terms as the one JsxCore provides. Only aliases landing
/// inside the views directory are taken: <c>dotnet:*</c> and the framework's declarations also live
/// in <c>paths</c>, and those are the import map's business.
/// </para>
/// </remarks>
public sealed class ViewAliases
{
    public static readonly ViewAliases None = new([]);

    private readonly IReadOnlyList<(string Prefix, string Suffix, string Target)> _patterns;

    private ViewAliases(IReadOnlyList<(string, string, string)> patterns) => _patterns = patterns;

    public bool IsEmpty => _patterns.Count == 0;

    /// <summary>Reads the aliases out of a generated tsconfig, if it is there and readable.</summary>
    public static ViewAliases ReadFrom(string tsConfigPath, string viewsDirectory)
    {
        if (string.IsNullOrEmpty(tsConfigPath) || !File.Exists(tsConfigPath))
        {
            return None;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(tsConfigPath));

            if (!document.RootElement.TryGetProperty("compilerOptions", out var compilerOptions)
                || !compilerOptions.TryGetProperty("paths", out var paths)
                || paths.ValueKind != JsonValueKind.Object)
            {
                return None;
            }

            var views = Path.GetFullPath(viewsDirectory);
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(tsConfigPath))!;
            var patterns = new List<(string, string, string)>();

            foreach (var entry in paths.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var target in entry.Value.EnumerateArray())
                {
                    if (target.GetString() is not { } value
                        || Resolve(configDirectory, value) is not { } resolved
                        || !IsUnder(resolved, views))
                    {
                        continue;
                    }

                    // One wildcard is all TypeScript allows, so a pattern is a prefix and a suffix.
                    var star = entry.Name.IndexOf('*');
                    if (star < 0)
                    {
                        patterns.Add((entry.Name, string.Empty, resolved));
                        break;
                    }

                    patterns.Add((entry.Name[..star], entry.Name[(star + 1)..], resolved));
                    break;
                }
            }

            return patterns.Count == 0 ? None : new ViewAliases(patterns);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // A configuration being rewritten underneath us just means nothing is rewritten.
            return None;
        }
    }

    /// <summary>
    /// The source file an aliased specifier names, or null when no alias claims it.
    /// </summary>
    public string? Resolve(string specifier)
    {
        foreach (var (prefix, suffix, target) in _patterns)
        {
            if (!specifier.StartsWith(prefix, StringComparison.Ordinal)
                || !specifier.EndsWith(suffix, StringComparison.Ordinal)
                || specifier.Length < prefix.Length + suffix.Length)
            {
                continue;
            }

            var matched = specifier[prefix.Length..(specifier.Length - suffix.Length)];
            var star = target.IndexOf('*');

            var resolved = star < 0
                ? target
                : target[..star] + matched.Replace('/', Path.DirectorySeparatorChar) + target[(star + 1)..];

            return Path.GetFullPath(resolved);
        }

        return null;
    }

    private static string? Resolve(string configDirectory, string target)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(
                configDirectory, target.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a path, wildcard and all, sits inside the views directory. The wildcard is dropped
    /// first so that the directory the pattern points at is what gets tested.
    /// </summary>
    private static bool IsUnder(string path, string root)
    {
        var star = path.IndexOf('*');
        var directory = star < 0 ? path : path[..star];

        return directory.StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                directory.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase);
    }
}
