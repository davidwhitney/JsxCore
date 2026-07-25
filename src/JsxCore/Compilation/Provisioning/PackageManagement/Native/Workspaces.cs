using System.Text.Json;
using System.Text.RegularExpressions;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

// A package inside this repository rather than one fetched from the registry. npm links these into
// node_modules instead of downloading them, which is what lets one package in a monorepo depend on
// another and see edits immediately.
public sealed record Workspace(string Name, string Version, string RelativePath, PackageManifest Manifest);

public static partial class Workspaces
{
    public static IReadOnlyList<Workspace> Discover(string root, PackageManifest? manifest)
    {
        if (manifest is null || Patterns(manifest) is not { Count: > 0 } patterns)
        {
            return [];
        }

        var found = new List<Workspace>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            foreach (var directory in Match(root, pattern))
            {
                if (!seen.Add(directory) || PackageManifest.In(directory) is not { } workspaceManifest)
                {
                    continue;
                }

                var name = workspaceManifest.Field("name");
                if (name is null)
                {
                    continue;
                }

                found.Add(new Workspace(
                    name,
                    workspaceManifest.Field("version") ?? "0.0.0",
                    Path.GetRelativePath(root, directory).Replace('\\', '/'),
                    workspaceManifest));
            }
        }

        return found;
    }

    private static IReadOnlyList<string> Patterns(PackageManifest manifest)
    {
        if (!manifest.Root.TryGetProperty("workspaces", out var workspaces))
        {
            return [];
        }

        // Either an array of globs, or an object with a "packages" array.
        var array = workspaces.ValueKind switch
        {
            JsonValueKind.Array => workspaces,
            JsonValueKind.Object when workspaces.TryGetProperty("packages", out var packages) => packages,
            _ => default
        };

        return array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList()
            : [];
    }

    // Only the globbing npm actually uses for workspaces: a literal path, or one * standing for a
    // single directory level, or ** for any depth.
    private static IEnumerable<string> Match(string root, string pattern)
    {
        var normalised = pattern.Replace('\\', '/').Trim('/');

        if (!normalised.Contains('*'))
        {
            var direct = Path.Combine(root, normalised.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(direct) ? [Path.GetFullPath(direct)] : [];
        }

        var regex = new Regex("^" + string.Join("/", normalised.Split('/').Select(segment => segment switch
        {
            "**" => ".+",
            "*" => "[^/]+",
            _ => Regex.Escape(segment).Replace("\\*", "[^/]*")
        })) + "$");

        var depth = normalised.Contains("**") ? SearchOption.AllDirectories : SearchOption.AllDirectories;

        return Directory.EnumerateDirectories(root, "*", depth)
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                        && !d.EndsWith($"{Path.DirectorySeparatorChar}node_modules"))
            .Where(d => regex.IsMatch(Path.GetRelativePath(root, d).Replace('\\', '/')))
            .Select(Path.GetFullPath);
    }

    // Everything the workspaces depend on, as if the root had declared it. That is what puts a
    // single copy at the top rather than one inside each workspace.
    public static IReadOnlyList<PackageRequest> DependenciesOf(IReadOnlyList<Workspace> workspaces)
    {
        var requests = new List<PackageRequest>();
        var names = workspaces.Select(w => w.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var workspace in workspaces)
        {
            foreach (var package in workspace.Manifest.Packages)
            {
                // A dependency on a sibling workspace is satisfied by the link, not the registry.
                if (!names.Contains(package.Name))
                {
                    requests.Add(new PackageRequest(package.Name, package.Range, package.Development));
                }
            }
        }

        return requests;
    }
}
