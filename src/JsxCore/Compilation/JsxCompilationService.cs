using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using JsxCore.TypeScript;
using Microsoft.Extensions.Logging;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Pipeline;
using JsxCore.Compilation.Provisioning;
using JsxCore.Compilation.Pipeline.Steps;
using JsxCore.Compilation.Pipeline.Steps.Compile;
using JsxCore.Compilation.Pipeline.Steps.Gather;
using JsxCore.Compilation.Pipeline.Steps.Prepare;

namespace JsxCore.Compilation;

public sealed record BuildState(string BuildId, CompilationResult Result);

public sealed class JsxCompilationService(
    JsxCoreOptions options,
    CompilationLayout layout,
    TypeScriptToolchain? toolchain,
    ILogger<JsxCompilationService> logger,
    PreactVendorStager? preact = null,
    ReactEntryStager? react = null,
    JsFramework framework = JsFramework.Preact,
    JsMinifier? minifier = null)
    : IDisposable
{
    private readonly JsxCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TypeScriptCompiler? _compiler = toolchain is null ? null : new TypeScriptCompiler(toolchain);
    private readonly ILogger<JsxCompilationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _compileLock = new(1, 1);

    private ViewWatcher? _watcher;
    private volatile BuildState? _state;
    private string _runtimeHash = string.Empty;
    private bool _disposed;

    public event Action<BuildState>? BuildCompleted;
    public CompilationLayout Layout { get; } = layout ?? throw new ArgumentNullException(nameof(layout));
    public string BuildId => _state?.BuildId ?? "0";

    /// <summary>
    /// The most recent build, with its diagnostics, or null before the first one.
    /// </summary>
    /// <remarks>
    /// The same value <see cref="BuildCompleted"/> carries, readable without having subscribed,
    /// which is what a health check or a diagnostics endpoint needs.
    /// </remarks>
    public BuildState? Current => _state;

    /// <summary>The compiler in use, or null when the application serves precompiled views.</summary>
    public TypeScriptToolchain? Toolchain => _compiler?.Toolchain;

    /// <summary>
    /// Everything a build does, in order. Gather finds what the project has, prepare puts what the
    /// compiler needs on disk, compile runs it.
    /// </summary>
    private BuildPipeline CreatePipeline() => new(
        new GatherProjectInputs(),
        new CheckDeclaredPackages(),
        new ExtractRuntimeAssets(),
        new WriteGlobalsModule(),
        new StagePreactRuntime(preact),
        new StageReactRuntime(react),
        new GenerateModelTypes(),
        new WriteCompilerConfig(framework),
        new CreateOutputDirectory(),
        new MinifyStagedAssets(minifier, preact?.Directory, react?.Directory),
        new CompileViews(async (fingerprint, token) =>
        {
            // Recorded before compiling: the build id folds in what the earlier steps produced.
            _runtimeHash = fingerprint;
            await CompileAsync(token).ConfigureAwait(false);
        }));

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var context = new BuildContext(_options, Layout, _logger, Precompiled: _compiler is null);
        _runtimeHash = await CreatePipeline().RunAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.Precompiled)
        {
            // Nothing was built: adopt whatever is already on disk as the current build.
            _state = new BuildState(NextBuildId(EmptySuccess), EmptySuccess);
            _logger.LogInformation("JsxCore is serving precompiled views from {Directory}.", Layout.OutputDirectory);
        }
    }

    private static readonly CompilationResult EmptySuccess = new(true, [], TimeSpan.Zero, string.Empty);

    public async Task<BuildState> CompileAsync(CancellationToken cancellationToken = default)
    {
        if (_compiler is null)
        {
            return _state ?? new BuildState("0", EmptySuccess);
        }

        await _compileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _compiler.CompileAsync(Layout, cancellationToken).ConfigureAwait(false);

            // An asset import is still spelled the way the view wrote it, which no browser can
            // load. Done whatever the compiler made of the views: a type error does not stop it
            // emitting, and a page that renders with a broken image is the worse outcome.
            LinkAssets();

            // Between compiling and taking the build id, so the id covers what is served rather
            // than what tsc emitted. Only on success: minifying broken output helps nobody.
            if (minifier is not null && result.Succeeded)
            {
                var count = minifier.MinifyDirectory(Layout.OutputDirectory);
                if (count > 0)
                {
                    _logger.LogInformation(
                        "JsxCore minified {Count} compiled view(s) with esbuild {Version}.",
                        count, minifier.Version);
                }
            }

            var state = new BuildState(NextBuildId(result), result);

            LogResult(result);

            if (!result.Succeeded && _options.TypeChecking == TypeCheckingMode.Error)
            {
                // Keep the previous good build serving rather than leaving the app with nothing.
                _state ??= state;
                throw new JsxCompilationException(
                    $"JsxCore failed to compile views:{Environment.NewLine}{result.FormatDiagnostics()}",
                    result.Errors);
            }

            _state = state;
            return state;
        }
        finally
        {
            _compileLock.Release();
        }
    }

    private void LinkAssets()
    {
        var linked = ViewAssetLinker.Link(Layout);
        _views = linked.Manifest;

        if (linked.Linked > 0)
        {
            _logger.LogDebug("JsxCore linked {Count} static asset import(s).", linked.Linked);
        }

        // Reported rather than guessed at. Nothing else will say so, because the compiler has no
        // opinion about a scheme it does not know, and it would otherwise surface much later as a
        // module the browser cannot load.
        foreach (var specifier in linked.Unresolved.Distinct(StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "JsxCore could not resolve the asset import '{Specifier}': there is no such file " +
                "under {WebRoot}. The path is relative to your web root, exactly as the URL is.",
                specifier, Layout.WebRoot);
        }
    }

    private volatile ViewManifest? _views;

    /// <summary>
    /// What the linker recorded about each compiled module: its stylesheets, and the render mode
    /// its directive prologue asks for.
    /// </summary>
    /// <remarks>
    /// Read from disk when nothing compiled in this process, which is the precompiled case: the
    /// build recorded it and publishing carried it, exactly as it did the views themselves.
    /// </remarks>
    public ViewManifest Views => _views ??= ViewManifest.ReadFrom(Layout.OutputDirectory);

    private void LogResult(CompilationResult result)
    {
        if (result.Succeeded)
        {
            _logger.LogInformation("JsxCore compiled views in {Duration}ms.", (int)result.Duration.TotalMilliseconds);
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error when _options.TypeChecking == TypeCheckingMode.Error:
                    _logger.LogError("JsxCore: {Diagnostic}", diagnostic);
                    break;
                case DiagnosticSeverity.Error:
                case DiagnosticSeverity.Warning:
                    _logger.LogWarning("JsxCore: {Diagnostic}", diagnostic);
                    break;
                default:
                    _logger.LogInformation("JsxCore: {Diagnostic}", diagnostic);
                    break;
            }
        }
    }

    private readonly Dictionary<string, (long Ticks, long Length, byte[] Hash)> _fileHashes =
        new(StringComparer.OrdinalIgnoreCase);

    private string NextBuildId(CompilationResult result)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(_runtimeHash));

        if (Directory.Exists(Layout.OutputDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(Layout.OutputDirectory, "*.js", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(Layout.OutputDirectory, file)));
                hash.AppendData(HashOf(file));
            }
        }

        // Failed builds must still change the id so clients stop using stale modules.
        if (!result.Succeeded)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(result.FormatDiagnostics()));
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..10].ToLowerInvariant();
    }

    // Still a content hash, but only of files that changed. A watch rebuild recompiles a handful
    // of modules and used to reread every one of them to work out the build id.
    private byte[] HashOf(string file)
    {
        try
        {
            var info = new FileInfo(file);
            var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);

            if (_fileHashes.TryGetValue(file, out var cached) &&
                cached.Ticks == stamp.Ticks && cached.Length == stamp.Length)
            {
                return cached.Hash;
            }

            var hash = SHA256.HashData(File.ReadAllBytes(file));
            _fileHashes[file] = (stamp.Ticks, stamp.Length, hash);
            return hash;
        }
        catch (IOException)
        {
            // A file being rewritten underneath us just means the next build wins.
            return [];
        }
    }

    public string? ResolveCompiledModule(LocatedView view)
    {
        var path = Path.Combine(Layout.OutputDirectory, view.ModuleRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = new ViewWatcher(
            Layout.ViewsDirectory,
            _options.Extensions.Concat([".ts", ".js", ".json"]).ToList(),
            _logger);

        _watcher.Changed += RecompileFromWatcherAsync;
        _watcher.Start();
    }

    private async Task RecompileFromWatcherAsync()
    {
        try
        {
            var state = await CompileAsync().ConfigureAwait(false);
            BuildCompleted?.Invoke(state);
        }
        catch (JsxCompilationException ex)
        {
            // In Error mode a failed watch build must not take the process down.
            _logger.LogError(ex, "JsxCore recompilation failed.");
            var failed = _state;
            if (failed is not null)
            {
                BuildCompleted?.Invoke(failed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JsxCore recompilation failed unexpectedly.");
        }
    }

    [SuppressMessage("Usage", "CA1816", Justification = "Sealed type with no finalizer.")]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _watcher?.Dispose();
        _compileLock.Dispose();
    }
}
