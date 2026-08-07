using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation;

public sealed record LocatedView(string ViewName, string SourcePath, string RelativePath)
{
    public string ModuleRelativePath => RelativePath + ".js";
}

public sealed class ViewLocator(
    JsxCoreOptions options,
    CompilationLayout layout,
    string contentRoot,
    ILogger<ViewLocator>? logger = null)
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
                if (Create(viewName, candidate) is { } located)
                {
                    return Remember(viewName, controllerName, areaName, located);
                }

                // Outside the views tree, so nothing compiled it and nothing can serve it. Not
                // found rather than fatal: another view engine is entitled to claim a name this one
                // cannot, and taking the request down would end that. Said out loud all the same,
                // because a file that exists and is passed over is not something to work out from
                // silence.
                WarnOnce(viewName, candidate);
                continue;
            }

            // A published application has compiled output but no sources, so fall back to looking
            // for what the source would have compiled to.
            if (CompiledFor(candidate) is { } && RelativePathFor(candidate) is { } relative)
            {
                return Remember(viewName, controllerName, areaName,
                    new LocatedView(viewName, candidate, relative));
            }
        }

        return null;
    }

    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explains a file that was found and could not be used, once per file.
    /// </summary>
    /// <remarks>
    /// Once, because resolution is not cached when it fails: without this a request in a loop for a
    /// view that cannot render would say so on every one of them.
    /// </remarks>
    private void WarnOnce(string viewName, string path)
    {
        lock (_warned)
        {
            if (!_warned.Add(path))
            {
                return;
            }
        }

        logger?.LogWarning(
            "JsxCore found '{Path}' for the view '{ViewName}', but it is outside the views directory " +
            "'{ViewsDirectory}'. JsxCore compiles that directory, so a file above it has no compiled " +
            "module and cannot be rendered; the view is being treated as not found. Move it under the " +
            "views directory, or set JsxCoreOptions.ViewsDirectory to a directory that contains it.",
            path, viewName, _layout.ViewsDirectory);
    }

    private LocatedView Remember(string viewName, string? controller, string? area, LocatedView view)
    {
        _resolved[(viewName, controller, area)] = view;
        return view;
    }

    private string? CompiledFor(string sourcePath)
    {
        if (RelativePathFor(sourcePath) is not { } relative)
        {
            return null;
        }

        var compiled = Path.Combine(
            _layout.OutputDirectory,
            (relative + ".js").Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(compiled) ? compiled : null;
    }

    /// <summary>
    /// Where a source file sits within the views tree, without its extension, or null when it does
    /// not sit within it at all.
    /// </summary>
    /// <remarks>
    /// The compiler's root is the views directory, and every URL a module is served from is derived
    /// from this path, so a file above the root has no module and no URL. Answering null rather
    /// than a path full of "../" is what lets that be reported where it can be explained.
    /// </remarks>
    private string? RelativePathFor(string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath);
        var relative = Path.GetRelativePath(_layout.ViewsDirectory, full).Replace('\\', '/');

        // Rooted means there was no relative route at all, which on Windows is a different drive.
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var extension = Path.GetExtension(relative);
        return extension.Length == 0 ? relative : relative[..^extension.Length];
    }

    private LocatedView? Create(string viewName, string sourcePath) =>
        RelativePathFor(sourcePath) is { } relative
            ? new LocatedView(viewName, Path.GetFullPath(sourcePath), relative)
            : null;

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

        // "/Home/Index" is a view name written with a leading slash, not a path. Trimmed here
        // because a format is a relative shape and would otherwise produce a doubled separator,
        // which the guard below then skips, leaving the name resolving to nothing.
        var name = viewName.TrimStart('/');

        foreach (var format in formats)
        {
            var relative = format
                .Replace("{ViewsDirectory}", _options.ViewsDirectory, StringComparison.Ordinal)
                .Replace("{0}", name, StringComparison.Ordinal)
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

    /// <summary>
    /// Whether a name is a file to open rather than a view to look up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extension decides it. <c>Home/Index</c> is a view name and goes through the location
    /// formats, so it picks up the Shared fallback and the controller and area shapes;
    /// <c>Home/Index.tsx</c> names a file and is opened.
    /// </para>
    /// <para>
    /// That is what makes a leading slash unambiguous. Everywhere except Windows an absolute path
    /// and a views-relative name are spelled the same way, so nothing about the slash can tell them
    /// apart: <c>/Home/Index</c> is a view, and <c>/srv/app/Views/Home/Index.tsx</c> is a path.
    /// <c>~/</c> is always a path, with or without an extension, because it exists to say so.
    /// </para>
    /// </remarks>
    private bool IsExplicitPath(string viewName) =>
        viewName.StartsWith("~/", StringComparison.Ordinal)
        || HasViewExtension(viewName)
        || (Path.IsPathRooted(viewName) && !viewName.StartsWith('/'));

    private bool HasViewExtension(string viewName) =>
        _options.Extensions.Any(extension => viewName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> ExplicitPathCandidates(string viewName)
    {
        var hasExtension = HasViewExtension(viewName);

        // Rooted, so it names one file and there is nowhere else to look. Covers both an absolute
        // path and a Windows drive path; "~/" is not rooted and falls through.
        if (Path.IsPathRooted(viewName))
        {
            foreach (var candidate in WithExtensions(root: null, viewName, hasExtension))
            {
                yield return candidate;
            }

            yield break;
        }

        var trimmed = viewName.TrimStart('~').TrimStart('/');

        // "~/Views/Home/Index.tsx" is content-root relative. Anything else is relative to the views
        // directory first, since that is where views live, and to the content root after.
        var roots = viewName.StartsWith("~/", StringComparison.Ordinal)
            ? new[] { _contentRoot }
            : [_layout.ViewsDirectory, _contentRoot];

        foreach (var root in roots)
        {
            foreach (var candidate in WithExtensions(root, trimmed, hasExtension))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// The path, or the path with each configured extension when it was named without one.
    /// </summary>
    private IEnumerable<string> WithExtensions(string? root, string path, bool hasExtension)
    {
        var native = path.Replace('/', Path.DirectorySeparatorChar);
        var combined = root is null ? native : Path.Combine(root, native);

        if (hasExtension)
        {
            yield return Path.GetFullPath(combined);
            yield break;
        }

        foreach (var extension in _options.Extensions)
        {
            yield return Path.GetFullPath(combined + extension);
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
            if (_options.Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                && Create(Path.GetFileNameWithoutExtension(path), path) is { } view)
            {
                yield return view;
            }
        }
    }
}
