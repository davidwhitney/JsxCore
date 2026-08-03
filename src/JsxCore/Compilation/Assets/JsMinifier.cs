using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// Minifies JavaScript on its way to the browser, by running esbuild over it.
/// </summary>
/// <remarks>
/// Every method here degrades to doing nothing. A smaller payload is worth having and never worth
/// an outage, so a missing binary, a crash or a syntax esbuild dislikes leaves the original source
/// in place and logs it, rather than failing a build or a request.
/// <para>
/// Work is batched into one process per call. esbuild starts in a few milliseconds but a package
/// graph can run to hundreds of files, and that difference is the whole cost of the feature.
/// </para>
/// </remarks>
public sealed class JsMinifier(EsbuildToolchain toolchain, ILogger logger)
{
    private readonly EsbuildToolchain _toolchain =
        toolchain ?? throw new ArgumentNullException(nameof(toolchain));

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string Version => _toolchain.Version;

    /// <summary>
    /// Minifies every <c>.js</c> file under <paramref name="root"/>, in place.
    /// </summary>
    /// <returns>How many files were rewritten, which is zero when anything went wrong.</returns>
    /// <param name="preserveFormat">
    /// Leave each file the kind of module it already is. Required for anything out of
    /// node_modules: forcing ESM on a CommonJS package cannot convert its <c>require</c> calls, and
    /// JsxCore wraps those itself at serve time, so the original text is what must survive.
    /// </param>
    public int MinifyDirectory(string root, bool preserveFormat = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        var files = Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
        {
            return 0;
        }

        // --allow-overwrite is what makes this in place: outdir is the directory the inputs came
        // from, so each file is replaced by its own minified form rather than being bundled.
        //
        // --outbase is not optional. Without it esbuild derives output paths from the common
        // parent of whatever it was handed, so a lone Home/Index.js lands at the root as
        // Index.js: the original survives unminified and a stray module appears beside it.
        return Run([.. files, $"--outdir={root}", $"--outbase={root}", "--allow-overwrite"], root, preserveFormat)
            ? files.Count
            : 0;
    }

    /// <summary>
    /// Minifies sources held in memory, keyed by whatever the caller uses to identify them.
    /// </summary>
    /// <remarks>
    /// esbuild reads and writes files, so these go to a staging directory and come back. Keys are
    /// not used as file names: a key here is an asset URL, which may contain characters a file
    /// system will not take, so each source is staged under its index instead.
    /// </remarks>
    public IReadOnlyDictionary<string, string> MinifySources(
        IReadOnlyDictionary<string, string> sources, string stagingDirectory)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        if (sources.Count == 0)
        {
            return sources;
        }

        var ordered = sources.Keys.ToList();
        var staged = new List<string>(ordered.Count);

        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            Directory.CreateDirectory(stagingDirectory);

            for (var index = 0; index < ordered.Count; index++)
            {
                var path = Path.Combine(stagingDirectory, index + ".js");
                File.WriteAllText(path, sources[ordered[index]]);
                staged.Add(path);
            }

            if (!Run([.. staged, $"--outdir={stagingDirectory}", $"--outbase={stagingDirectory}",
                      "--allow-overwrite"], stagingDirectory))
            {
                return sources;
            }

            var minified = new Dictionary<string, string>(sources.Count, StringComparer.Ordinal);
            for (var index = 0; index < ordered.Count; index++)
            {
                minified[ordered[index]] = File.ReadAllText(staged[index]);
            }

            return minified;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("JsxCore could not minify npm assets: {Reason}", exception.Message);
            return sources;
        }
    }

    /// <summary>
    /// Invokes esbuild. Arguments are passed as a list because a package path can contain spaces.
    /// </summary>
    private bool Run(IReadOnlyList<string> arguments, string workingDirectory, bool preserveFormat = false)
    {
        // Modules in, modules out, for what JsxCore compiled: those are ESM and saying so keeps a
        // file that happens to look like a script from coming back as one. Never for a package,
        // where the format is the package's to decide.
        string[] format = preserveFormat ? [] : ["--format=esm"];

        var result = _toolchain.Run(
            [.. arguments, "--minify", .. format, "--log-level=warning"], workingDirectory);

        switch (result.Outcome)
        {
            case ToolOutcome.Exited when result.ExitCode == 0:
                return true;

            case ToolOutcome.TimedOut:
                _logger.LogWarning("JsxCore gave up waiting for esbuild; assets are served unminified.");
                return false;

            case ToolOutcome.CouldNotStart:
                _logger.LogWarning("JsxCore could not run esbuild: {Reason}", result.StandardError);
                return false;

            default:
                _logger.LogWarning(
                    "JsxCore could not minify with esbuild (exit code {ExitCode}); assets are served " +
                    "unminified.{NewLine}{Error}", result.ExitCode, Environment.NewLine, result.Output);
                return false;
        }
    }
}
