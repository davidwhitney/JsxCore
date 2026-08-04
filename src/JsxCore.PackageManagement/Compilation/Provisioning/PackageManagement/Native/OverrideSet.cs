using System.Text.Json;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

// package.json "overrides": a way to force a version on something you do not depend on directly,
// usually to get out from under a transitive dependency you cannot otherwise move.
//
//   "overrides": {
//     "semver": "^7.5.2",                      forces semver everywhere
//     "foo": { ".": "1.0.0", "bar": "2.0.0" }  forces foo, and bar wherever foo needs it
//   }
public sealed class OverrideSet
{
    private readonly IReadOnlyDictionary<string, string> _global;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _scoped;

    public static readonly OverrideSet Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    private OverrideSet(
        IReadOnlyDictionary<string, string> global,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> scoped)
    {
        _global = global;
        _scoped = scoped;
    }

    public bool IsEmpty => _global.Count == 0 && _scoped.Count == 0;

    public static OverrideSet From(PackageManifest? manifest)
    {
        if (manifest is null
            || !manifest.Root.TryGetProperty("overrides", out var overrides)
            || overrides.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var scoped = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var entry in overrides.EnumerateObject())
        {
            // "name@range" only applies to dependents asking within that range. Treated as a plain
            // name, which over-applies rather than under-applies, and is reported as unsupported.
            var name = entry.Name.Split('@', 2, StringSplitOptions.None) is [var bare, _] && !entry.Name.StartsWith('@')
                ? bare
                : entry.Name;

            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                global[name] = entry.Value.GetString() ?? "";
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var children = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var child in entry.Value.EnumerateObject())
            {
                if (child.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                // "." is the version for the parent itself; anything else applies inside it.
                if (child.Name == ".")
                {
                    global[name] = child.Value.GetString() ?? "";
                }
                else
                {
                    children[child.Name] = child.Value.GetString() ?? "";
                }
            }

            if (children.Count > 0)
            {
                scoped[name] = children;
            }
        }

        return new OverrideSet(global, scoped);
    }

    // The range to use instead, or null to leave the dependency alone. A rule written inside a
    // parent wins over a blanket one, because it is the more specific statement.
    public string? RangeFor(string name, string? dependentName)
    {
        if (dependentName is not null
            && _scoped.TryGetValue(dependentName, out var children)
            && children.TryGetValue(name, out var scoped))
        {
            return scoped;
        }

        return _global.TryGetValue(name, out var global) ? global : null;
    }
}
