namespace JsxCore.Compilation.Modules;

public sealed class NodeModulesLayout
{
    private const string NodeModules = "node_modules";

    private readonly IReadOnlyList<string> _searchDirectories;

    private NodeModulesLayout(IReadOnlyList<string> searchDirectories)
    {
        _searchDirectories = searchDirectories;
        Roots = searchDirectories
            .Select(directory => Path.Combine(directory, NodeModules))
            .Where(Directory.Exists)
            .ToList();
    }

    public static NodeModulesLayout For(string contentRoot, IEnumerable<string>? additionalRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string directory)
        {
            if (seen.Add(directory))
            {
                directories.Add(directory);
            }
        }

        foreach (var extra in additionalRoots ?? [])
        {
            Add(Path.GetFullPath(extra));
        }

        // Walking up is what makes a solution level install work, and the application base covers
        // a published layout whose content root has been moved.
        var current = new DirectoryInfo(Path.GetFullPath(contentRoot));
        while (current is not null)
        {
            Add(current.FullName);
            current = current.Parent;
        }

        Add(AppContext.BaseDirectory);

        return new NodeModulesLayout(directories);
    }

    public IReadOnlyList<string> Roots { get; }

    public IReadOnlyList<string> SearchDirectories => _searchDirectories;

    public string? FindFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var suffix = relativePath.Replace('/', Path.DirectorySeparatorChar);

        foreach (var root in Roots)
        {
            var candidate = Path.Combine(root, suffix);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? FindPackage(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var suffix = name.Replace('/', Path.DirectorySeparatorChar);

        foreach (var root in Roots)
        {
            var candidate = Path.Combine(root, suffix);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public IReadOnlyList<string> CandidatePaths(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var suffix = Path.Combine(NodeModules, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return _searchDirectories.Select(directory => Path.Combine(directory, suffix)).ToList();
    }

    // A package's own dependencies can sit inside it, so resolution starts beside the importer.
    public IEnumerable<string> RootsFor(string? importerPath)
    {
        if (importerPath is not null)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(importerPath) ?? ".");
            while (directory is not null)
            {
                var nested = Path.Combine(directory.FullName, NodeModules);
                if (Directory.Exists(nested))
                {
                    yield return nested;
                }
                directory = directory.Parent;
            }
        }

        foreach (var root in Roots)
        {
            yield return root;
        }
    }

    public bool Contains(string path) => Roots.Any(root => IsUnder(path, root));

    public string? RootOf(string path) => Roots.FirstOrDefault(root => IsUnder(path, root));

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
