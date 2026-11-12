
namespace JsxCore.Compilation.Modules;

public sealed record NpmClientAsset(string Url, string Content, string ContentType);

public sealed record NpmClientManifest(
    IReadOnlyDictionary<string, string> ImportMap,
    IReadOnlyDictionary<string, NpmClientAsset> Assets)
{
    /// <summary>
    /// The stylesheets each import-map specifier pulls in, transitively and in import order, as URLs.
    /// A page links them because a browser cannot import CSS from a module without an attribute.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Stylesheets { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}

/// <param name="transform">
/// Applied to every asset once the graph is closed, keyed by URL. Batching the whole graph is what
/// makes minification affordable: one pass over hundreds of modules rather than one per module.
/// </param>
public sealed class NpmClientGraph(
    NodeModuleResolver npm,
    Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? transform = null)
{
    private const string JavaScriptContentType = "text/javascript; charset=utf-8";
    private const string StylesheetContentType = "text/css; charset=utf-8";

    private readonly NodeModuleResolver _npm = npm ?? throw new ArgumentNullException(nameof(npm));
    private readonly BuildScopedCache<NpmClientManifest> _cache = new();
    private readonly HashSet<string> _notExported = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> NotExported => _notExported;

    public NpmClientManifest ForBuild(
        string buildId,
        string outputDirectory,
        string assetBase,
        IReadOnlyCollection<string>? reserved = null,
        IReadOnlyCollection<string>? seed = null) =>
        _cache.Get(buildId, () => Build(outputDirectory, assetBase, reserved, seed));

    /// <param name="seed">
    /// Specifiers to include whether or not a view mentions them. A framework's own entry points
    /// are not views, so nothing here would otherwise discover what they import.
    /// </param>
    private NpmClientManifest Build(
        string outputDirectory,
        string assetBase,
        IReadOnlyCollection<string>? reserved,
        IReadOnlyCollection<string>? seed = null)
    {
        var importMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var assets = new Dictionary<string, NpmClientAsset>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(outputDirectory))
        {
            return new NpmClientManifest(importMap, assets);
        }

        var discovered = Directory
            .EnumerateFiles(outputDirectory, "*.js", SearchOption.AllDirectories)
            .Select(ReadOrEmpty)
            .SelectMany(ModuleTransform.FindSpecifiers);

        // The views themselves are served as compiled, so their bare specifiers have to be
        // satisfied by the import map rather than by rewriting.
        foreach (var group in new[] { seed ?? [], discovered })
        {
            foreach (var specifier in group)
            {
                if (!NodeModuleResolver.IsBareSpecifier(specifier) || importMap.ContainsKey(specifier))
                {
                    continue;
                }

                if (reserved is not null && reserved.Contains(specifier))
                {
                    continue;
                }

                if (_npm.Resolve(specifier) is not { } resolved)
                {
                    continue;
                }

                // Checked after resolving, because only a package that is actually installed can
                // meaningfully be withheld. devDependencies build the application and are not
                // published, so exporting one would work in development and fail in production.
                var package = NodeModuleResolver.PackageNameOf(specifier);
                if (_npm.RuntimeDependencies.Count > 0 && !_npm.RuntimeDependencies.Contains(package))
                {
                    _notExported.Add(package);
                    continue;
                }

                importMap[specifier] = assetBase + "/npm/" + UrlFor(resolved.Path);
                entries[specifier] = resolved.Path;
                if (seen.Add(resolved.Path))
                {
                    pending.Enqueue(resolved.Path);
                }
            }
        }

        while (pending.Count > 0)
        {
            var path = pending.Dequeue();
            var asset = Prepare(path, assetBase, out var dependencies);
            assets[asset.Url] = asset;
            edges[path] = dependencies;

            if (_npm.KindOf(path) == NodeModuleKind.Stylesheet)
            {
                var url = StylesheetUrlFor(path);
                assets[url] = new NpmClientAsset(url, ReadOrEmpty(path), StylesheetContentType);
            }

            foreach (var dependency in dependencies.Where(dependency => seen.Add(dependency)))
            {
                pending.Enqueue(dependency);
            }
        }

        var stylesheets = entries.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)StylesheetsFrom(pair.Value, edges)
                .Select(path => assetBase + "/npm/" + StylesheetUrlFor(path))
                .ToList(),
            StringComparer.Ordinal);

        return new NpmClientManifest(importMap, Transformed(assets)) { Stylesheets = stylesheets };
    }

    // Depth first in import order, which is the order a bundler would have emitted them in.
    private List<string> StylesheetsFrom(string entry, IReadOnlyDictionary<string, List<string>> edges)
    {
        var found = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(entry);

        while (stack.Count > 0)
        {
            var path = stack.Pop();
            if (!visited.Add(path))
            {
                continue;
            }

            if (_npm.KindOf(path) == NodeModuleKind.Stylesheet)
            {
                found.Add(path);
                continue;
            }

            if (edges.TryGetValue(path, out var dependencies))
            {
                for (var index = dependencies.Count - 1; index >= 0; index--)
                {
                    stack.Push(dependencies[index]);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Hands every asset to the transform at once and takes back what it returns. A key it does not
    /// answer for keeps its original content, so a partial result is still a working graph.
    /// </summary>
    private IReadOnlyDictionary<string, NpmClientAsset> Transformed(
        IReadOnlyDictionary<string, NpmClientAsset> assets)
    {
        if (transform is null || assets.Count == 0)
        {
            return assets;
        }

        // Only JavaScript: the transform treats everything it is given as a script.
        var replaced = transform(assets
            .Where(pair => pair.Value.ContentType == JavaScriptContentType)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Content, StringComparer.Ordinal));

        return assets.ToDictionary(
            pair => pair.Key,
            pair => replaced.TryGetValue(pair.Key, out var content) ? pair.Value with { Content = content } : pair.Value,
            StringComparer.Ordinal);
    }

    private NpmClientAsset Prepare(string path, string assetBase, out List<string> dependencies)
    {
        var shaped = ModuleTransform.Apply(
            path, _npm.KindOf(path), ReadOrEmpty(path), new BrowserSpecifierRewriter(_npm, path => assetBase + "/npm/" + UrlFor(path)));

        dependencies = shaped.Dependencies.ToList();
        return new NpmClientAsset(UrlFor(path), shaped.Source, JavaScriptContentType);
    }

    private string UrlFor(string path)
    {
        var full = Path.GetFullPath(path);

        for (var index = 0; index < _npm.SearchRoots.Count; index++)
        {
            var root = _npm.SearchRoots[index];
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
            // Imported as modules, so they are served as JavaScript under a name that says so.
            return index + "/" + (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                ? relative + ".js"
                : relative);
        }

        throw new JsxCoreException($"JsxCore resolved '{path}', which is not inside a known node_modules directory.");
    }

    private string StylesheetUrlFor(string path) => UrlFor(path)[..^".js".Length];

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

}
