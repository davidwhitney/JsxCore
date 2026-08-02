using System.Text.RegularExpressions;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// Turns <c>import logo from "dotnet:wwwroot/images/logo.svg"</c> into something a browser and the
/// server-side engine can both load, and records which stylesheets each view pulls in.
/// </summary>
/// <remarks>
/// <para>
/// The file is served by ASP.NET Core, from wwwroot, and is not touched: what a view is importing is
/// the URL, not the bytes. So a one-line module is generated for each imported asset, exporting that
/// URL, and the emitted import is pointed at it. TypeScript leaves the specifier exactly as
/// written, having no opinion about a scheme it does not know, which is why this runs on the output.
/// </para>
/// <para>
/// Runs after the compiler and before minification, so the build id is taken over what is actually
/// served rather than over what tsc emitted.
/// </para>
/// </remarks>
public static partial class ViewAssetLinker
{
    /// <summary>What a linking run did, and what it could not resolve.</summary>
    public sealed record Result(int Linked, ViewAssetManifest Manifest, IReadOnlyList<string> Unresolved)
    {
        public static readonly Result None = new(0, ViewAssetManifest.Empty, []);
    }

    public static Result Link(CompilationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Link(layout.OutputDirectory, layout.WebRoot);
    }

    public static Result Link(string outputDirectory, string webRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!Directory.Exists(outputDirectory))
        {
            return Result.None;
        }

        var context = new LinkContext(Path.GetFullPath(outputDirectory), Path.GetFullPath(webRoot));
        var manifest = new ViewAssetManifest();
        var linked = 0;

        foreach (var file in Directory
                     .EnumerateFiles(context.Output, "*.js", SearchOption.AllDirectories)
                     .Where(path => !context.IsGenerated(path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string source;
            try
            {
                source = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var module = new ModuleLinker(context, file);
            var rewritten = Specifier().Replace(source, module.Rewrite);

            linked += module.Linked;

            if (module.Imports.Count > 0 || module.Styles.Count > 0)
            {
                manifest.Modules[context.RelativeToOutput(file)] =
                    new ViewAssetModule(module.Imports, module.Styles);
            }

            if (rewritten != source)
            {
                AssetStage.WriteFileIfChanged(file, rewritten);
            }
        }

        context.PruneGenerated();
        WriteManifest(context.Output, manifest);

        return new Result(linked, manifest, context.Unresolved);
    }

    /// <summary>
    /// The manifest exists only when a view imports a stylesheet, so its absence is the fast path
    /// for the applications that do not.
    /// </summary>
    private static void WriteManifest(string root, ViewAssetManifest manifest)
    {
        var path = Path.Combine(root, ViewAssets.ManifestFileName);

        try
        {
            if (manifest.HasStyles)
            {
                AssetStage.WriteFileIfChanged(path, manifest.ToJson());
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Losing the manifest costs a page its stylesheet links, not its response.
        }
    }

    /// <summary>The roots this run works against, and what it has generated so far.</summary>
    private sealed class LinkContext(string output, string webRoot)
    {
        public string Output { get; } = output;

        private readonly string _generatedRoot = Path.Combine(output, ViewAssets.ModuleDirectory);
        private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _unresolved = [];

        /// <summary>Specifiers that named nothing in the web root, in the order they were met.</summary>
        public IReadOnlyList<string> Unresolved => _unresolved;

        public bool IsGenerated(string path) => Under(path, _generatedRoot);

        public string RelativeToOutput(string path) =>
            Path.GetRelativePath(Output, path).Replace(Path.DirectorySeparatorChar, '/');

        private static bool Under(string path, string root) =>
            path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Writes the module for an asset and returns it, or null when the web root holds no such
        /// file. Nothing is guessed: an import naming a file that is not there is reported rather
        /// than pointed at a URL that would 404 in a browser and nowhere else.
        /// </summary>
        public GeneratedModule? Generate(string specifier, string assetPath)
        {
            var file = Path.GetFullPath(Path.Combine(
                webRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));

            // "../" in the specifier must not reach outside the directory the application serves.
            if (!Under(file, webRoot) || !File.Exists(file))
            {
                _unresolved.Add(specifier);
                return null;
            }

            // The URL is the path within the web root, which is precisely what UseStaticFiles
            // serves it from. Normalised through the resolved file so "images/../images/logo.svg"
            // and "images/logo.svg" cannot become two URLs for one file.
            var url = "/" + Path.GetRelativePath(webRoot, file).Replace(Path.DirectorySeparatorChar, '/');

            var module = Path.Combine(_generatedRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) + ".js");

            Directory.CreateDirectory(Path.GetDirectoryName(module)!);
            AssetStage.WriteFileIfChanged(module, ViewAssets.ModuleSource(url));
            _written.Add(Path.GetFullPath(module));

            return new GeneratedModule(module, url);
        }

        /// <summary>Removes modules written for assets nothing imports any more.</summary>
        public void PruneGenerated()
        {
            if (!Directory.Exists(_generatedRoot))
            {
                return;
            }

            try
            {
                foreach (var stale in Directory
                             .EnumerateFiles(_generatedRoot, "*", SearchOption.AllDirectories)
                             .Where(path => !_written.Contains(Path.GetFullPath(path)))
                             .ToList())
                {
                    File.Delete(stale);
                }
            }
            catch (IOException)
            {
                // Left behind is only ever a module nothing imports.
            }
        }
    }

    /// <summary>A generated module: where it is, and the URL it exports.</summary>
    private sealed record GeneratedModule(string ModulePath, string Url);

    /// <summary>Rewrites one compiled module's specifiers, collecting what it imports as it goes.</summary>
    private sealed class ModuleLinker(LinkContext context, string file)
    {
        private readonly string _directory = Path.GetDirectoryName(file)!;

        public List<string> Imports { get; } = [];
        public List<string> Styles { get; } = [];
        public int Linked { get; private set; }

        public string Rewrite(Match match)
        {
            var specifier = match.Groups["spec"].Value;

            // A sibling module, which is the graph the stylesheet order comes from.
            if (specifier.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                if (specifier.StartsWith('.') && Resolve(specifier) is { } import)
                {
                    Imports.Add(import);
                }

                return match.Value;
            }

            if (ViewAssets.PathFor(specifier) is not { } assetPath || !ViewAssets.IsAsset(assetPath))
            {
                return match.Value;
            }

            if (context.Generate(specifier, assetPath) is not { } generated)
            {
                return match.Value;
            }

            if (assetPath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                Styles.Add(generated.Url);
            }

            Linked++;

            // Relative rather than the scheme it came in as: the browser resolves it against the
            // module's own URL, which already carries the build id, so nothing has to be told where
            // this application serves its modules from.
            var relative = Path.GetRelativePath(_directory, generated.ModulePath)
                .Replace(Path.DirectorySeparatorChar, '/');

            return match.Groups["lead"].Value
                   + match.Groups["quote"].Value
                   + (relative.StartsWith('.') ? relative : "./" + relative)
                   + match.Groups["quote"].Value;
        }

        /// <summary>
        /// Resolves a relative specifier to a path within the compiled output, or null when it
        /// climbs out of it.
        /// </summary>
        private string? Resolve(string specifier)
        {
            var combined = Path.GetFullPath(Path.Combine(
                _directory, specifier.Replace('/', Path.DirectorySeparatorChar)));

            return combined.StartsWith(
                context.Output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                ? context.RelativeToOutput(combined)
                : null;
        }
    }

    /// <summary>
    /// Matches the specifier of a static or dynamic import, and of a re-export, which reaches here
    /// through its <c>from</c>.
    /// </summary>
    [GeneratedRegex(@"(?<lead>\b(?:from|import)\b\s*\(?\s*)(?<quote>[""'])(?<spec>[^""'\n]*)\k<quote>")]
    private static partial Regex Specifier();
}
