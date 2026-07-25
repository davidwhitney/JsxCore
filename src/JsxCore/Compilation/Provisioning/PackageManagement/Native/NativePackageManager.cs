using System.Text.Json;
using System.Text.Json.Nodes;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public sealed class NativePackageManager(
    HttpClient? http = null,
    string registryUrl = "https://registry.npmjs.org",
    Action<string>? report = null) : IPackageManager
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Action<string> _report = report ?? (_ => { });

    public string Name => "native";

    // Nothing to find: this is the strategy that needs nothing installed. It is the reason the
    // interface exists, and the point where a machine without Node stops being stuck.
    public bool IsAvailable() => true;

    public PackageOperationResult CreateManifest(string directory)
    {
        var path = Path.Combine(directory, "package.json");
        if (File.Exists(path))
        {
            return PackageOperationResult.NothingToDo;
        }

        Directory.CreateDirectory(directory);

        var manifest = new JsonObject
        {
            ["name"] = SuggestName(directory),
            ["version"] = "1.0.0",
            ["private"] = true,
            ["dependencies"] = new JsonObject(),
            ["devDependencies"] = new JsonObject()
        };

        File.WriteAllText(path, manifest.ToJsonString(Indented) + Environment.NewLine);
        _report($"JsxCore: created {path}.");
        return PackageOperationResult.Ok("created package.json");
    }

    public PackageOperationResult RestoreFromLockFile(string directory)
    {
        var locked = LockFile.Read(directory);
        if (locked.Count == 0)
        {
            return PackageOperationResult.NothingToDo;
        }

        try
        {
            var installed = RestoreAsync(directory, locked).GetAwaiter().GetResult();
            return PackageOperationResult.Ok($"restored {installed} package(s) from the lock file");
        }
        catch (Exception ex) when (ex is JsxCoreException or HttpRequestException or IOException)
        {
            return PackageOperationResult.Failed("restore from the lock file", ex.Message);
        }
    }

    private async Task<int> RestoreAsync(string directory, IReadOnlyList<LockedPackage> locked)
    {
        var nodeModules = Path.Combine(directory, "node_modules");
        var installed = 0;

        // Nothing is resolved here: the lock file already decided every version, so this is a
        // download of exactly what it pins. That is the whole point of restoring from one.
        foreach (var package in locked.Where(p => p.RunsHere()))
        {
            var target = Path.Combine(nodeModules, package.Name.Replace('/', Path.DirectorySeparatorChar));
            _report($"JsxCore: fetching {package.Name}@{package.Version}");

            using var response = await _http.GetAsync(package.Resolved).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var tarball = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await PackageArchive.ExtractAsync(tarball, target, package.Integrity).ConfigureAwait(false);
            installed++;
        }

        return installed;
    }

    public PackageOperationResult InstallDeclared(string directory)
    {
        var manifest = PackageManifest.In(directory);
        if (manifest is null)
        {
            return PackageOperationResult.Failed("install declared packages", "there is no package.json.");
        }

        var requested = manifest.Packages
            .Select(p => new PackageRequest(p.Name, p.Range, p.Development))
            .ToList();

        return Install(directory, requested, save: false);
    }

    public PackageOperationResult Add(string directory, IReadOnlyCollection<PackageRequest> packages) =>
        packages.Count == 0 ? PackageOperationResult.NothingToDo : Install(directory, packages, save: true);

    private PackageOperationResult Install(string directory, IReadOnlyCollection<PackageRequest> requested, bool save)
    {
        try
        {
            var installed = InstallAsync(directory, requested, save).GetAwaiter().GetResult();
            return PackageOperationResult.Ok($"installed {installed} package(s)");
        }
        catch (Exception ex) when (ex is JsxCoreException or HttpRequestException or IOException)
        {
            return PackageOperationResult.Failed("install packages", ex.Message);
        }
    }

    private async Task<int> InstallAsync(string directory, IReadOnlyCollection<PackageRequest> requested, bool save)
    {
        var registry = new NpmRegistry(_http, registryUrl);
        var manifest = PackageManifest.In(directory);
        var overrides = OverrideSet.From(manifest);
        var workspaces = Workspaces.Discover(directory, manifest);

        // Adding a package resolves everything already declared along with it. Resolving the new
        // package alone would produce a tree, and therefore a lock file, describing only that
        // package, and npm rejects a lock file that omits what package.json declares. A range
        // given now wins over the declared one, which is what makes adding a package upgrade it.
        var declared = save && manifest is not null
            ? manifest.Packages
                .Where(package => !requested.Any(r => string.Equals(r.Name, package.Name, StringComparison.Ordinal)))
                .Select(package => new PackageRequest(package.Name, package.Range, package.Development))
            : [];

        // Every workspace is linked whether or not anything depends on it, because declaring one
        // is what asks for the link. Their dependencies are then resolved as though the root had
        // declared them, which is what puts one copy at the top instead of one inside each.
        var all = requested
            .Concat(declared)
            .Concat(workspaces.Select(w => new PackageRequest(w.Name)))
            .Concat(Workspaces.DependenciesOf(workspaces))
            .ToList();

        var placed = await new PackageResolver(registry, overrides, workspaces, _http)
            .ResolveAsync(all).ConfigureAwait(false);

        // A tree where something cannot resolve what it needs is worse than no tree: it installs,
        // and then fails at run time inside somebody else's package.
        if (PackageResolver.Validate(placed, overrides) is { Count: > 0 } problems)
        {
            throw new JsxCoreException(
                "JsxCore resolved a dependency tree that does not work: " +
                string.Join(" ", problems.Take(3)) +
                (problems.Count > 3 ? $" ({problems.Count - 3} more)" : ""));
        }

        foreach (var entry in PackageResolver.InstallableOn(placed))
        {
            var package = entry.Package;
            // The resolver decided the path, including any nesting, so this just unpacks there.
            var target = Path.Combine(directory, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (entry.Workspace is { } workspace)
            {
                Link(directory, target, workspace);
                continue;
            }

            _report($"JsxCore: fetching {package.Name}@{package.Version}");

            if (entry.Git is { } git)
            {
                using var response = await _http.GetAsync(git.ArchiveUrl).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var archive = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await PackageArchive.ExtractAsync(archive, target, integrity: null).ConfigureAwait(false);
                continue;
            }

            await using var tarball = await registry.DownloadAsync(package).ConfigureAwait(false);
            await PackageArchive.ExtractAsync(tarball, target, package.Integrity).ConfigureAwait(false);
        }

        if (save)
        {
            Save(directory, requested, placed);
        }

        // Written from the whole resolution, not from what this machine happened to unpack, so the
        // lock file is usable on any platform.
        // Re-read, because saving may have just created or changed it.
        if (PackageManifest.In(directory) is { } written)
        {
            LockFile.Write(directory, written, placed);
        }

        return placed.Count;
    }

    private static void Save(
        string directory,
        IReadOnlyCollection<PackageRequest> requested,
        IReadOnlyList<PlacedPackage> placed)
    {
        var path = Path.Combine(directory, "package.json");
        var manifest = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : new JsonObject();

        foreach (var package in requested)
        {
            var block = package.Development ? "devDependencies" : "dependencies";
            manifest[block] ??= new JsonObject();

            // Recorded as a caret range on what was actually installed, which is what npm writes
            // when a range was not pinned explicitly.
            manifest[block]!.AsObject()[package.Name] = package.VersionRange.Length > 0
                ? package.VersionRange
                : "^" + placed.First(p => p.Name == package.Name && !p.IsNested).Package.Version;
        }

        File.WriteAllText(path, manifest.ToJsonString(Indented) + Environment.NewLine);
    }

    // A workspace is linked rather than copied, so editing it is visible without reinstalling.
    private static void Link(string root, string target, Workspace workspace)
    {
        var source = Path.GetFullPath(Path.Combine(root, workspace.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (Directory.Exists(target) || File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(target);
            }
            else
            {
                Directory.Delete(target, recursive: true);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateSymbolicLink(target, source);
    }

    private static string SuggestName(string directory)
    {
        var name = new DirectoryInfo(Path.GetFullPath(directory)).Name.ToLowerInvariant();
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return cleaned.Length > 0 ? cleaned : "app";
    }

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };
}
