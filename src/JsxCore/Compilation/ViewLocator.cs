using System.Collections.Concurrent;

namespace JsxCore.Compilation;

public sealed record LocatedView(string ViewName, string SourcePath, string RelativePath)
{
    public string ModuleRelativePath => RelativePath + ".js";
}

public sealed class ViewLocator(JsxCoreOptions options, CompilationLayout layout, string contentRoot)
{
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly CompilationLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly string _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));

    private readonly ConcurrentDictionary<(string, string?, string?), LocatedView?> _resolved = new();

    // Dropped whenever the views are rebuilt, since that is when a name can start or stop
    // resolving. The build id is the key for everything derived from a build.
    public void Invalidate() => _resolved.Clear();

    public LocatedView? Find(string viewName, string? controllerName, string? areaName, out IReadOnlyList<string> searchedLocations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        // Resolving walks the location formats calling into the file system for each candidate,
        // once per request for a view that has not moved since the last build.
        if (_resolved.TryGetValue((viewName, controllerName, areaName), out var cached) && cached is not null)
        {
            searchedLocations = [];
            return cached;
        }

        var searched = new List<string>();
        searchedLocations = searched;

        foreach (var candidate in CandidatePaths(viewName, controllerName, areaName))
        {
            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                return Remember(viewName, controllerName, areaName, Create(viewName, candidate));
            }

            // A published application has compiled output but no sources, so fall back to looking
            // for what the source would have compiled to.
            if (CompiledFor(candidate) is not null)
            {
                return Remember(viewName, controllerName, areaName,
                    new LocatedView(viewName, candidate, RelativePathFor(candidate)));
            }
        }

        return null;
    }

    private LocatedView Remember(string viewName, string? controller, string? area, LocatedView view)
    {
        _resolved[(viewName, controller, area)] = view;
        return view;
    }

    private string? CompiledFor(string sourcePath)
    {
        var relative = RelativePathFor(sourcePath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var compiled = Path.Combine(
            _layout.OutputDirectory,
            (relative + ".js").Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(compiled) ? compiled : null;
    }

    private string RelativePathFor(string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath);
        var relative = Path.GetRelativePath(_layout.ViewsDirectory, full).Replace('\\', '/');
        var extension = Path.GetExtension(relative);
        return extension.Length == 0 ? relative : relative[..^extension.Length];
    }

    private LocatedView Create(string viewName, string sourcePath) =>
        new(viewName, Path.GetFullPath(sourcePath), RelativePathFor(sourcePath));

    private IEnumerable<string> CandidatePaths(string viewName, string? controllerName, string? areaName)
    {
        if (IsExplicitPath(viewName))
        {
            foreach (var path in ExplicitPathCandidates(viewName))
            {
                yield return path;
            }
            yield break;
        }

        var formats = string.IsNullOrEmpty(areaName)
            ? _options.ViewLocationFormats
            : _options.AreaViewLocationFormats.Concat(_options.ViewLocationFormats);

        foreach (var format in formats)
        {
            var relative = format
                .Replace("{ViewsDirectory}", _options.ViewsDirectory, StringComparison.Ordinal)
                .Replace("{0}", viewName, StringComparison.Ordinal)
                .Replace("{1}", controllerName ?? string.Empty, StringComparison.Ordinal)
                .Replace("{2}", areaName ?? string.Empty, StringComparison.Ordinal);

            // A format that needs a controller or area we do not have collapses to a doubled
            // separator; skip rather than probing a nonsense path.
            if (relative.Contains("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var extension in _options.Extensions)
            {
                yield return Path.GetFullPath(Path.Combine(_contentRoot, relative.Replace('/', Path.DirectorySeparatorChar) + extension));
            }
        }
    }

    private static bool IsExplicitPath(string viewName) =>
        viewName.StartsWith("~/", StringComparison.Ordinal)
        || viewName.StartsWith('/')
        || Path.IsPathRooted(viewName);

    private IEnumerable<string> ExplicitPathCandidates(string viewName)
    {
        if (Path.IsPathRooted(viewName) && !viewName.StartsWith('/'))
        {
            yield return Path.GetFullPath(viewName);
            yield break;
        }

        var trimmed = viewName.TrimStart('~').TrimStart('/');
        var hasExtension = _options.Extensions.Any(e => trimmed.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        // "~/Views/Home/Index.tsx" is content-root relative; "/Home/Index" is views-relative.
        var roots = viewName.StartsWith("~/", StringComparison.Ordinal)
            ? new[] { _contentRoot }
            : [_layout.ViewsDirectory, _contentRoot];

        foreach (var root in roots)
        {
            if (hasExtension)
            {
                yield return Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));
                continue;
            }

            foreach (var extension in _options.Extensions)
            {
                yield return Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar) + extension));
            }
        }
    }

    public IEnumerable<LocatedView> EnumerateAll()
    {
        if (!Directory.Exists(_layout.ViewsDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_layout.ViewsDirectory, "*", SearchOption.AllDirectories))
        {
            if (_options.Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                yield return Create(Path.GetFileNameWithoutExtension(path), path);
            }
        }
    }
}
