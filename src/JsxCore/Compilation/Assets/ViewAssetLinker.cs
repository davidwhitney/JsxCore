using JsxCore.Compilation.Modules;

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
public static class ViewAssetLinker
{
    /// <summary>What a linking run did, and what it could not resolve.</summary>
    /// <param name="Unresolved">Rooted asset imports naming a file the web root does not hold.</param>
    /// <param name="Misplaced">
    /// Asset imports written relatively, or as a package sub-path. They type check against the
    /// ambient declarations, which cannot be narrowed to rooted specifiers, and then resolve to
    /// nothing.
    /// </param>
    public sealed record Result(
        int Linked,
        ViewManifest Manifest,
        IReadOnlyList<string> Unresolved,
        IReadOnlyList<string> Misplaced)
    {
        public static readonly Result None = new(0, ViewManifest.Empty, [], []);
    }

    public static Result Link(CompilationLayout layout) => Link(layout, esbuild: null, npm: null);

    public static Result Link(
        CompilationLayout layout,
        EsbuildToolchain? esbuild,
        NodeModuleResolver? npm,
        Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return Link(
            layout.OutputDirectory,
            layout.WebRoot,
            layout.ViewsDirectory,
            ViewAliases.ReadFrom(layout.TsConfigPath, layout.ViewsDirectory),
            esbuild,
            npm,
            report);
    }

    public static Result Link(
        string outputDirectory,
        string webRoot,
        string? viewsDirectory = null,
        ViewAliases? aliases = null,
        EsbuildToolchain? esbuild = null,
        NodeModuleResolver? npm = null,
        Action<string>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!Directory.Exists(outputDirectory))
        {
            return Result.None;
        }

        var context = new LinkContext(
            Path.GetFullPath(outputDirectory),
            Path.GetFullPath(webRoot),
            viewsDirectory is null ? null : Path.GetFullPath(viewsDirectory),
            aliases ?? ViewAliases.None);
        var manifest = new ViewManifest();
        var linked = 0;

        // Scanned once, rewritten once. A stylesheet cannot be pointed at until it has been
        // processed, and processing is one batch for the whole application, so every module is
        // read and scanned before any of them is rewritten.
        var modules = Directory
            .EnumerateFiles(context.Output, "*.js", SearchOption.AllDirectories)
            .Where(path => !context.IsGenerated(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(ScannedModule.Read)
            .OfType<ScannedModule>()
            .ToList();

        var styles = ProcessStyles(context, modules, viewsDirectory, esbuild, npm, report);

        foreach (var module in modules)
        {
            var linker = new ModuleLinker(context, module.Path, styles);
            var rewritten = ModuleSpecifiers.Rewrite(module.Source, module.Specifiers, linker.Rewrite);

            linked += linker.Linked;

            // The prologue is untouched either way, but this is the one point the compiler's own
            // output is in hand.
            var mode = ViewDirectives.Parse(module.Source);

            if (linker.Imports.Count > 0 || linker.Styles.Count > 0 || mode is not null)
            {
                manifest.Modules[context.RelativeToOutput(module.Path)] =
                    new ViewModule(linker.Imports, linker.Styles, mode);
            }

            if (!ReferenceEquals(rewritten, module.Source))
            {
                AssetStage.WriteFileIfChanged(module.Path, rewritten);
            }
        }

        context.PruneGenerated();
        WriteManifest(context.Output, manifest);

        return new Result(linked, manifest, context.Unresolved, context.Misplaced);
    }

    /// <summary>A compiled module, its text, and the specifiers it imports from.</summary>
    /// <remarks>
    /// The scan is the expensive part and every stage wants the same answer, so it is done once
    /// here rather than repeated by each of them.
    /// </remarks>
    private sealed record ScannedModule(string Path, string Source, IReadOnlyList<ModuleSpecifier> Specifiers)
    {
        public static ScannedModule? Read(string path)
        {
            try
            {
                var source = File.ReadAllText(path);
                return new ScannedModule(path, source, ModuleSpecifiers.Scan(source));
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Finds every stylesheet the compiled modules import and puts each somewhere it can be
    /// served, scoping the class names of the ones named <c>*.module.css</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, ProcessedStyle> ProcessStyles(
        LinkContext context,
        IReadOnlyList<ScannedModule> modules,
        string? viewsDirectory,
        EsbuildToolchain? esbuild,
        NodeModuleResolver? npm,
        Action<string>? report)
    {
        if (viewsDirectory is null)
        {
            return new Dictionary<string, ProcessedStyle>(StringComparer.Ordinal);
        }

        var imports = modules
            .SelectMany(module => module.Specifiers
                .Where(specifier => specifier.Value.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                .Select(specifier => (specifier.Value, module.Path)))
            .ToList();

        var pipeline = new ViewStyles(context.Output, viewsDirectory, esbuild, npm, report);
        var processed = pipeline.Process(imports);

        // Kept so the sweep at the end of linking does not delete what was just produced: these are
        // written by the style pipeline rather than by the context that owns the tree.
        foreach (var path in pipeline.Written)
        {
            context.Keep(path);
        }

        foreach (var specifier in pipeline.Unresolved)
        {
            context.RecordMisplaced(specifier);
        }

        return processed;
    }

    /// <summary>
    /// The manifest exists only when a view imports a stylesheet or declares a directive, so its
    /// absence is the fast path for the applications that do neither.
    /// </summary>
    private static void WriteManifest(string root, ViewManifest manifest)
    {
        var path = Path.Combine(root, ViewAssets.ManifestFileName);

        try
        {
            if (manifest.HasContent)
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
    private sealed class LinkContext(string output, string webRoot, string? views, ViewAliases aliases)
    {
        public string Output { get; } = output;

        /// <summary>
        /// The compiled module an aliased specifier names, relative to the output, or null when no
        /// alias claims it or it lands outside the views directory.
        /// </summary>
        public string? ResolveAlias(string specifier)
        {
            if (views is null || aliases.IsEmpty || aliases.Resolve(specifier) is not { } source)
            {
                return null;
            }

            if (!Under(source, views))
            {
                return null;
            }

            // The output mirrors the views tree, and every view compiles to .js whatever it was
            // written as.
            var relative = Path.ChangeExtension(Path.GetRelativePath(views, source), ".js");

            return File.Exists(Path.Combine(Output, relative))
                ? relative.Replace(Path.DirectorySeparatorChar, '/')
                : null;
        }

        // Everything generated lives under one root, so what to keep and what to delete is one
        // question asked once rather than one per kind of output.
        private readonly string _distRoot = ViewAssets.PathUnder(output, ViewAssets.DistDirectory);
        private readonly string _moduleRoot = ViewAssets.PathUnder(output, ViewAssets.ModuleDirectory);
        private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _unresolved = [];

        /// <summary>Specifiers that named nothing in the web root, in the order they were met.</summary>
        public IReadOnlyList<string> Unresolved => _unresolved;

        /// <summary>Asset imports written some way other than as a URL, so nothing serves them.</summary>
        public IReadOnlyList<string> Misplaced => _misplaced;

        private readonly List<string> _misplaced = [];

        public void RecordMisplaced(string specifier) => _misplaced.Add(specifier);

        public bool IsGenerated(string path) => Under(path, _distRoot);

        public string RelativeToOutput(string path) =>
            Path.GetRelativePath(Output, path).Replace(Path.DirectorySeparatorChar, '/');

        public static bool Under(string path, string root) =>
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

            var module = ModulePath("assets/" + url.TrimStart('/'));

            Directory.CreateDirectory(Path.GetDirectoryName(module)!);
            AssetStage.WriteFileIfChanged(module, ViewAssets.ModuleSource(url));
            _written.Add(Path.GetFullPath(module));

            return new GeneratedModule(module, url);
        }

        /// <summary>
        /// Writes the module a stylesheet import resolves to, and returns where it is.
        /// </summary>
        public string? GenerateStyleModule(string specifier, ProcessedStyle style)
        {
            // Two kinds, kept apart. One names a URL the application already serves, alongside the
            // other assets that do; the other names a stylesheet this build produced. Sharing a
            // subtree would let a wwwroot path and a views path collide on the same module.
            var key = style.Rooted
                ? "assets/" + style.Url.TrimStart('/')
                : "styles/" + style.Url[(ViewAssets.StyleDirectory.Length + 1)..];

            var module = ModulePath(key);

            Directory.CreateDirectory(Path.GetDirectoryName(module)!);
            AssetStage.WriteFileIfChanged(module, ViewAssets.StyleModuleSource(style.Url, style.Names));
            _written.Add(Path.GetFullPath(module));

            return module;
        }

        private string ModulePath(string key) =>
            Path.Combine(_moduleRoot, key.Replace('/', Path.DirectorySeparatorChar) + ".js");

        /// <summary>Keeps a file this run produced, so pruning does not take it away again.</summary>
        public void Keep(string path) => _written.Add(Path.GetFullPath(path));

        /// <summary>
        /// Removes everything under the generated root that this run did not write.
        /// </summary>
        /// <remarks>
        /// One sweep over one tree, which is the whole point of there being one tree. It covers
        /// modules for assets nothing imports any more, stylesheets no view reaches, and the
        /// staging directory left behind by a run of esbuild that failed part way.
        /// </remarks>
        public void PruneGenerated()
        {
            if (!Directory.Exists(_distRoot))
            {
                return;
            }

            try
            {
                foreach (var stale in Directory
                             .EnumerateFiles(_distRoot, "*", SearchOption.AllDirectories)
                             .Where(path => !_written.Contains(Path.GetFullPath(path)))
                             .ToList())
                {
                    File.Delete(stale);
                }

                // Deepest first, so a directory emptied by the sweep above is gone before its
                // parent is considered.
                foreach (var directory in Directory
                             .EnumerateDirectories(_distRoot, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length)
                             .ToList())
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
            }
            catch (IOException)
            {
                // Left behind is only ever something nothing imports.
            }
        }
    }

    /// <summary>A generated module: where it is, and the URL it exports.</summary>
    private sealed record GeneratedModule(string ModulePath, string Url);

    /// <summary>Rewrites one compiled module's specifiers, collecting what it imports as it goes.</summary>
    private sealed class ModuleLinker(
        LinkContext context, string file, IReadOnlyDictionary<string, ProcessedStyle> styles)
    {
        private readonly string _directory = Path.GetDirectoryName(file)!;

        public List<string> Imports { get; } = [];
        public List<string> Styles { get; } = [];
        public int Linked { get; private set; }

        /// <summary>
        /// What this specifier should become, or null to leave it exactly as the compiler wrote it.
        /// </summary>
        public string? Rewrite(ModuleSpecifier match)
        {
            var specifier = match.Value;

            // A sibling module, which is the graph the stylesheet order comes from.
            if (specifier.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                if (specifier.StartsWith('.') && Resolve(specifier) is { } import)
                {
                    Imports.Add(import);
                }

                return null;
            }

            // A stylesheet, wherever it came from: the web root, beside a view, or a package.
            if (specifier.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                return styles.TryGetValue(specifier, out var style) ? RewriteStyle(specifier, style) : null;
            }

            if (ViewAssets.PathFor(specifier) is not { } assetPath || !ViewAssets.IsAsset(assetPath))
            {
                // An asset named some other way resolves to nothing, and saying so is the only
                // chance anyone gets: the declarations cannot tell the two spellings apart.
                if (ViewAssets.IsMisplacedAsset(specifier))
                {
                    context.RecordMisplaced(specifier);
                    return null;
                }

                return RewriteAlias(specifier);
            }

            if (context.Generate(specifier, assetPath) is not { } generated)
            {
                return null;
            }

            if (assetPath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                Styles.Add(generated.Url);
            }

            return PointAt(generated.ModulePath);
        }

        /// <summary>
        /// Points a stylesheet import at the module written for it, and records the URL the
        /// document has to link.
        /// </summary>
        /// <remarks>
        /// A CSS module's import has a value, the scoped class names, so its module exports the
        /// map esbuild produced. An ordinary stylesheet binds nothing, so its module exists only
        /// to give the import something to resolve to.
        /// </remarks>
        private string? RewriteStyle(string specifier, ProcessedStyle style)
        {
            if (context.GenerateStyleModule(specifier, style) is not { } generated)
            {
                return null;
            }

            Styles.Add(style.Url);
            return PointAt(generated);
        }

        /// <summary>
        /// Turns an aliased specifier into a relative path to the compiled module it names, which
        /// is what both the browser and the server module loader can resolve.
        /// </summary>
        private string? RewriteAlias(string specifier)
        {
            if (specifier.StartsWith('.') || context.ResolveAlias(specifier) is not { } module)
            {
                return null;
            }

            Imports.Add(module);
            return PointAt(Path.Combine(context.Output, module));
        }

        /// <summary>
        /// The specifier that reaches <paramref name="target"/> from this module.
        /// </summary>
        /// <remarks>
        /// Relative rather than the scheme it came in as: the browser resolves it against the
        /// module's own URL, which already carries the build id, so nothing has to be told where
        /// this application serves its modules from.
        /// </remarks>
        private string PointAt(string target)
        {
            Linked++;

            var relative = Path.GetRelativePath(_directory, target).Replace(Path.DirectorySeparatorChar, '/');
            return relative.StartsWith('.') ? relative : "./" + relative;
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

}
