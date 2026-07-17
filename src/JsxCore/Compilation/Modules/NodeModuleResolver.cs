using System.Text.Json;

namespace JsxCore.Compilation.Modules;

public enum NodeModuleKind
{
    EsModule,
    CommonJs,
    Json
}

public sealed record ResolvedNodeModule(string Path, NodeModuleKind Kind, string? PackageRoot);

public sealed class NodeModuleResolver
{
    private static readonly string[] Conditions = ["import", "module", "browser", "default"];
    private static readonly string[] Extensions = [".mjs", ".js", ".cjs", ".json"];

    private readonly NodeModulesLayout _layout;
    private readonly Dictionary<string, PackageManifest?> _manifests = new(StringComparer.OrdinalIgnoreCase);

    public NodeModuleResolver(NodeModulesLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public NodeModuleResolver(string contentRoot, IEnumerable<string>? additionalRoots = null)
        : this(NodeModulesLayout.For(contentRoot, additionalRoots))
    {
    }

    public IReadOnlyList<string> SearchRoots => _layout.Roots;

    public IReadOnlySet<string> RuntimeDependencies => _runtimeDependencies ??= ReadRuntimeDependencies();
    private IReadOnlySet<string>? _runtimeDependencies;

    private IReadOnlySet<string> ReadRuntimeDependencies()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in _layout.Roots)
        {
            var directory = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
            if (directory is null || ReadManifest(directory) is not { } manifest)
            {
                continue;
            }

            names.UnionWith(manifest.RuntimeNames);
        }

        return names;
    }

    public static string PackageNameOf(string specifier)
    {
        var parts = specifier.Split('/');
        return specifier.StartsWith('@') && parts.Length >= 2 ? parts[0] + "/" + parts[1] : parts[0];
    }

    // Shape as well as prefix: specifiers are read out of compiled JavaScript by pattern, and
    // compiled JSX sets prose beside element calls, so text like from ", _jsx(" can look like one.
    public static bool IsBareSpecifier(string specifier) =>
        specifier.Length > 0
        && !specifier.StartsWith('.')
        && !specifier.StartsWith('/')
        && !specifier.StartsWith('#')
        && !Path.IsPathRooted(specifier)
        && !specifier.Contains("://", StringComparison.Ordinal)
        && specifier.All(c => !char.IsWhiteSpace(c) && c is not (',' or '(' or ')' or '{' or '}' or ';' or '<' or '>'));

    public ResolvedNodeModule? Resolve(string specifier, string? importerPath = null)
    {
        if (!IsBareSpecifier(specifier))
        {
            return null;
        }

        var (packageName, subPath) = SplitSpecifier(specifier);

        foreach (var root in _layout.RootsFor(importerPath))
        {
            var packageDirectory = Path.Combine(root, packageName.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(packageDirectory))
            {
                continue;
            }

            var file = ResolveWithinPackage(packageDirectory, subPath);
            if (file is not null)
            {
                return new ResolvedNodeModule(file, KindOf(file), root);
            }
        }

        return null;
    }

    public ResolvedNodeModule? ResolveFrom(string specifier, string importerPath) =>
        IsBareSpecifier(specifier) ? Resolve(specifier, importerPath) : ResolveRelative(specifier, importerPath);

    public ResolvedNodeModule? ResolveRelative(string specifier, string importerPath)
    {
        var directory = Path.GetDirectoryName(importerPath);
        if (directory is null)
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(directory, specifier.Replace('/', Path.DirectorySeparatorChar)));
        var file = ProbeFile(candidate);
        if (file is null)
        {
            return null;
        }

        return new ResolvedNodeModule(file, KindOf(file), _layout.RootOf(file));
    }

    private static (string Package, string SubPath) SplitSpecifier(string specifier)
    {
        var parts = specifier.Split('/');
        if (specifier.StartsWith('@') && parts.Length >= 2)
        {
            var scoped = parts[0] + "/" + parts[1];
            var rest = string.Join('/', parts.Skip(2));
            return (scoped, rest.Length == 0 ? "." : "./" + rest);
        }

        var tail = string.Join('/', parts.Skip(1));
        return (parts[0], tail.Length == 0 ? "." : "./" + tail);
    }

    private string? ResolveWithinPackage(string packageDirectory, string subPath)
    {
        var manifest = ReadManifest(packageDirectory);

        if (manifest is { } package && package.TryGetExports(out var exports))
        {
            var target = ResolveExports(exports, subPath);
            if (target is not null)
            {
                return ProbeFile(Path.Combine(packageDirectory, target.TrimStart('.', '/').Replace('/', Path.DirectorySeparatorChar)));
            }

            // An exports map that does not cover the subpath is a deliberate refusal, but older
            // packages pair it with a usable file layout, so fall through rather than give up.
        }

        if (subPath != ".")
        {
            return ProbeFile(Path.Combine(packageDirectory, subPath.TrimStart('.', '/').Replace('/', Path.DirectorySeparatorChar)));
        }

        foreach (var field in new[] { "module", "main" })
        {
            if (manifest?.Field(field) is { } value)
            {
                var file = ProbeFile(Path.Combine(packageDirectory, value.Replace('/', Path.DirectorySeparatorChar)));
                if (file is not null)
                {
                    return file;
                }
            }
        }

        return ProbeFile(Path.Combine(packageDirectory, "index"));
    }

    private static string? ResolveExports(JsonElement exports, string subPath)
    {
        if (exports.ValueKind == JsonValueKind.String)
        {
            return subPath == "." ? exports.GetString() : null;
        }

        if (exports.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // A map whose keys are all conditions rather than subpaths is shorthand for ".".
        var isSubPathMap = exports.EnumerateObject().Any(p => p.Name.StartsWith('.'));
        if (!isSubPathMap)
        {
            return subPath == "." ? SelectCondition(exports) : null;
        }

        if (exports.TryGetProperty(subPath, out var exact))
        {
            return SelectCondition(exact);
        }

        // Wildcard subpaths, for example "./features/*": "./dist/features/*.js".
        foreach (var candidate in exports.EnumerateObject())
        {
            var star = candidate.Name.IndexOf('*');
            if (star < 0)
            {
                continue;
            }

            var prefix = candidate.Name[..star];
            var suffix = candidate.Name[(star + 1)..];
            if (!subPath.StartsWith(prefix, StringComparison.Ordinal) || !subPath.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var wildcard = subPath[prefix.Length..(subPath.Length - suffix.Length)];
            var target = SelectCondition(candidate.Value);
            if (target is not null)
            {
                return target.Replace("*", wildcard, StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static string? SelectCondition(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (SelectCondition(item) is { } fromArray)
                    {
                        return fromArray;
                    }
                }
                return null;

            case JsonValueKind.Object:
                foreach (var condition in Conditions)
                {
                    if (element.TryGetProperty(condition, out var value) && SelectCondition(value) is { } resolved)
                    {
                        return resolved;
                    }
                }
                return null;

            default:
                return null;
        }
    }

    private static string? ProbeFile(string candidate)
    {
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        foreach (var extension in Extensions)
        {
            if (File.Exists(candidate + extension))
            {
                return Path.GetFullPath(candidate + extension);
            }
        }

        if (Directory.Exists(candidate))
        {
            foreach (var extension in Extensions)
            {
                var index = Path.Combine(candidate, "index" + extension);
                if (File.Exists(index))
                {
                    return Path.GetFullPath(index);
                }
            }
        }

        return null;
    }

    public NodeModuleKind KindOf(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".mjs") return NodeModuleKind.EsModule;
        if (extension == ".cjs") return NodeModuleKind.CommonJs;
        if (extension == ".json") return NodeModuleKind.Json;

        var declared = NearestPackageType(path) == "module" ? NodeModuleKind.EsModule : NodeModuleKind.CommonJs;

        // Packages do get this wrong, and the syntax is unambiguous enough to trust over metadata.
        if (declared == NodeModuleKind.CommonJs && LooksLikeEsModule(path))
        {
            return NodeModuleKind.EsModule;
        }

        return declared;
    }

    private static bool LooksLikeEsModule(string path)
    {
        try
        {
            var source = File.ReadAllText(path);
            var hasCommonJs = source.Contains("module.exports", StringComparison.Ordinal)
                              || source.Contains("require(", StringComparison.Ordinal);
            var hasEsm = System.Text.RegularExpressions.Regex.IsMatch(
                source, @"^\s*(export\s|import\s|export\{|import\{)", System.Text.RegularExpressions.RegexOptions.Multiline);

            return hasEsm && !hasCommonJs;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private string? NearestPackageType(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path) ?? ".");
        while (directory is not null)
        {
            var manifest = ReadManifest(directory.FullName);
            if (manifest is { } package)
            {
                return package.Type;
            }

            if (string.Equals(directory.Name, "node_modules", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            directory = directory.Parent;
        }

        return null;
    }

    private PackageManifest? ReadManifest(string directory)
    {
        if (_manifests.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var manifest = PackageManifest.In(directory);
        _manifests[directory] = manifest;
        return manifest;
    }

    public bool IsInsideNodeModules(string path) => _layout.Contains(path);
}
