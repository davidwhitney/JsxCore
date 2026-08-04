using System.Text.Json;
using System.Text.Json.Nodes;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public sealed record LockedPackage(
    string Path,
    string Name,
    string Version,
    string Resolved,
    string Integrity,
    bool Development,
    bool Optional,
    IReadOnlyList<string> OperatingSystems,
    IReadOnlyList<string> Architectures)
{
    public bool RunsHere() =>
        new RegistryPackage(Name, SemanticVersion.Parse(Version), Resolved, Integrity,
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            OperatingSystems, Architectures, new Dictionary<string, string>(),
            new Dictionary<string, string>(), new HashSet<string>()).RunsHere();
}

public static class LockFile
{
    public const string FileName = "package-lock.json";
    private const int Version = 3;

    public static string PathIn(string directory) => System.IO.Path.Combine(directory, FileName);

    public static void Write(
        string directory,
        PackageManifest manifest,
        IReadOnlyList<PlacedPackage> placed)
    {
        var root = new JsonObject
        {
            ["name"] = manifest.Field("name") ?? new DirectoryInfo(System.IO.Path.GetFullPath(directory)).Name
        };

        if (manifest.Field("version") is { } version)
        {
            root["version"] = version;
        }

        root["lockfileVersion"] = Version;
        root["requires"] = true;

        var packages = new JsonObject { [""] = RootEntry(manifest, root["name"]!.GetValue<string>()) };

        // Keyed by where the package actually goes, which is the whole point of the file: a nested
        // copy and a top level one of the same package are different entries.
        foreach (var entry in placed.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            packages[entry.Path] = Entry(entry);

            // The workspace itself is an entry too, keyed by where it lives rather than by where
            // it is linked from, which is how npm describes a monorepo.
            if (entry.Workspace is { } workspace)
            {
                packages[workspace.RelativePath] = WorkspaceEntry(workspace);
            }
        }

        root["packages"] = packages;
        File.WriteAllText(PathIn(directory), root.ToJsonString(Indented) + Environment.NewLine);
    }

    private static JsonObject RootEntry(PackageManifest manifest, string name)
    {
        var entry = new JsonObject { ["name"] = name };

        if (manifest.Field("version") is { } version)
        {
            entry["version"] = version;
        }

        // npm checks these against package.json and refuses to install if they disagree.
        foreach (var (block, packages) in new[]
                 {
                     ("dependencies", manifest.Dependencies),
                     ("devDependencies", manifest.DevDependencies)
                 })
        {
            var declared = packages.ToList();
            if (declared.Count == 0)
            {
                continue;
            }

            var map = new JsonObject();
            foreach (var package in declared.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                map[package.Name] = package.Range;
            }
            entry[block] = map;
        }

        return entry;
    }

    private static JsonObject Entry(PlacedPackage resolved)
    {
        // A workspace is a pointer at a directory in this repository, so there is nothing to fetch
        // and nothing to verify. npm writes the same two fields and its own entry for the target.
        if (resolved.Workspace is { } workspace)
        {
            return new JsonObject { ["resolved"] = workspace.RelativePath, ["link"] = true };
        }

        var entry = new JsonObject();

        if (resolved.IsAlias)
        {
            entry["name"] = resolved.Package.Name;
        }

        foreach (var (key, value) in new JsonObject
        {
            ["version"] = resolved.Package.Version.ToString(),
            ["resolved"] = resolved.Git?.ResolvedUrl ?? resolved.Package.TarballUrl,
            ["integrity"] = resolved.Package.Integrity
        }.ToList())
        {
            entry[key] = value?.DeepClone();
        }

        if (resolved.Package.Architectures.Count > 0)
        {
            entry["cpu"] = new JsonArray([.. resolved.Package.Architectures.Select(a => (JsonNode)a!)]);
        }

        if (resolved.Development)
        {
            entry["dev"] = true;
        }

        if (resolved.Optional)
        {
            entry["optional"] = true;
        }

        if (resolved.Package.OperatingSystems.Count > 0)
        {
            entry["os"] = new JsonArray([.. resolved.Package.OperatingSystems.Select(o => (JsonNode)o!)]);
        }

        if (resolved.Package.Engines.Count > 0)
        {
            var engines = new JsonObject();
            foreach (var (name, range) in resolved.Package.Engines.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                engines[name] = range;
            }
            entry["engines"] = engines;
        }

        // The two blocks are kept apart, and an optional entry wins where a name is in both. npm
        // reads anything under "dependencies" as required, so recording a per platform package
        // there makes every other platform's copy a hard requirement and the install fails on the
        // first one that does not match the machine.
        Block(entry, "dependencies",
            resolved.Package.Dependencies.Where(d => !resolved.Package.OptionalDependencies.ContainsKey(d.Key)));

        Block(entry, "optionalDependencies", resolved.Package.OptionalDependencies);
        Block(entry, "peerDependencies", resolved.Package.PeerDependencies);

        return entry;
    }

    private static void Block(JsonObject entry, string name, IEnumerable<KeyValuePair<string, string>> packages)
    {
        var declared = packages.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (declared.Count == 0)
        {
            return;
        }

        var map = new JsonObject();
        foreach (var (package, range) in declared)
        {
            map[package] = range;
        }

        entry[name] = map;
    }

    private static JsonObject WorkspaceEntry(Workspace workspace)
    {
        var entry = new JsonObject { ["name"] = workspace.Name, ["version"] = workspace.Version };

        foreach (var (block, packages) in new[]
                 {
                     ("dependencies", workspace.Manifest.Dependencies),
                     ("devDependencies", workspace.Manifest.DevDependencies)
                 })
        {
            var declared = packages.ToList();
            if (declared.Count == 0)
            {
                continue;
            }

            var map = new JsonObject();
            foreach (var package in declared.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                map[package.Name] = package.Range;
            }
            entry[block] = map;
        }

        return entry;
    }

    public static IReadOnlyList<LockedPackage> Read(string directory)
    {
        var path = PathIn(directory);
        if (!File.Exists(path))
        {
            return [];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return [];
        }

        if (root?["lockfileVersion"]?.GetValue<int>() is not (2 or 3)
            || root["packages"] is not JsonObject packages)
        {
            return [];
        }

        var locked = new List<LockedPackage>();
        foreach (var (key, value) in packages)
        {
            // The empty key is the project itself, which is not something to install.
            if (key.Length == 0 || value is not JsonObject entry)
            {
                continue;
            }

            var marker = key.LastIndexOf("node_modules/", StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var name = key[(marker + "node_modules/".Length)..];
            // A link has nothing to download; the target is already on disk.
            if (entry["link"]?.GetValue<bool>() == true
                || entry["resolved"]?.GetValue<string>() is not { Length: > 0 } url)
            {
                continue;
            }

            locked.Add(new LockedPackage(
                key,
                name,
                entry["version"]?.GetValue<string>() ?? "0.0.0",
                url,
                entry["integrity"]?.GetValue<string>() ?? "",
                entry["dev"]?.GetValue<bool>() ?? false,
                entry["optional"]?.GetValue<bool>() ?? false,
                Strings(entry["os"]),
                Strings(entry["cpu"])));
        }

        return locked;
    }

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(v => v?.GetValue<string>() ?? "")] : [];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };
}
