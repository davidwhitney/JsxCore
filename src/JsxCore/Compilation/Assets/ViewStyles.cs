using System.Diagnostics;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Assets;

/// <summary>A stylesheet a view imported, once it has somewhere to be served from.</summary>
/// <param name="Url">Where the browser fetches it, relative to the asset base unless rooted.</param>
/// <param name="Names">
/// The scoped class names a CSS module exports, as JavaScript object source, or null for a
/// stylesheet that is imported for its effect alone.
/// </param>
public sealed record ProcessedStyle(string Url, bool Rooted, string? Names = null);

/// <summary>
/// Turns the stylesheets views import into something served, scoping the class names of the ones
/// named <c>*.module.css</c>.
/// </summary>
/// <remarks>
/// <para>
/// Three places a stylesheet can come from, and only one of them is already served. A rooted
/// <c>/css/site.css</c> is a file in the web root, which <c>UseStaticFiles</c> handles and this
/// leaves alone. One beside a view, or one inside an npm package, is neither served nor scoped
/// until something does it, which is this.
/// </para>
/// <para>
/// The work is esbuild's, which JsxCore already restores to minify with. It scopes
/// <c>*.module.css</c> class names, leaves <c>:global(...)</c> alone, resolves <c>composes</c>, and
/// passes ordinary stylesheets through unchanged. Being the same native binary, it costs no Node
/// and runs in single-digit milliseconds.
/// </para>
/// <para>
/// Everything goes through one invocation. That is not only for speed: esbuild disambiguates the
/// scoped names it generates within a run, so two components that both have a
/// <c>Card.module.css</c> get distinct class names only if they are processed together.
/// </para>
/// </remarks>
public sealed class ViewStyles(
    string outputDirectory,
    string viewsDirectory,
    EsbuildToolchain? esbuild,
    NodeModuleResolver? npm,
    Action<string>? report = null)
{
    /// <summary>Where processed stylesheets are written, within the compiled output.</summary>
    public const string Directory = "_css";

    private const string ModuleSuffix = ".module.css";

    private readonly string _output = Path.GetFullPath(outputDirectory);
    private readonly string _views = Path.GetFullPath(viewsDirectory);

    /// <summary>Specifiers that named no stylesheet anything could find.</summary>
    public IReadOnlyList<string> Unresolved => _unresolved;
    private readonly List<string> _unresolved = [];

    /// <summary>
    /// Resolves and processes every stylesheet specifier, keyed by the module that imported it so
    /// a relative one is resolved from the right place.
    /// </summary>
    public IReadOnlyDictionary<string, ProcessedStyle> Process(
        IReadOnlyCollection<(string Specifier, string FromModule)> imports)
    {
        ArgumentNullException.ThrowIfNull(imports);

        var results = new Dictionary<string, ProcessedStyle>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (specifier, fromModule) in imports)
        {
            if (results.ContainsKey(specifier) || sources.ContainsKey(specifier))
            {
                continue;
            }

            // Already served, at the URL the rest of the application uses for the same file.
            if (ViewAssets.PathFor(specifier) is not null)
            {
                results[specifier] = new ProcessedStyle(specifier, Rooted: true);
                continue;
            }

            if (Locate(specifier, fromModule) is { } source)
            {
                sources[specifier] = source;
            }
            else
            {
                _unresolved.Add(specifier);
            }
        }

        foreach (var (specifier, style) in Compile(sources))
        {
            results[specifier] = style;
        }

        return results;
    }

    /// <summary>The stylesheet a specifier names, wherever it lives.</summary>
    private string? Locate(string specifier, string fromModule)
    {
        if (specifier.StartsWith('.'))
        {
            // The output mirrors the views tree, so a relative specifier resolves against the
            // source directory the importing module was compiled from.
            var directory = Path.GetDirectoryName(Path.Combine(
                _views, Path.GetRelativePath(_output, fromModule))) ?? _views;

            var resolved = Path.GetFullPath(Path.Combine(
                directory, specifier.Replace('/', Path.DirectorySeparatorChar)));

            return File.Exists(resolved) && Under(resolved, _views) ? resolved : null;
        }

        // A package stylesheet, as a third-party component's own styles are.
        return npm?.Resolve(specifier, fromModule)?.Path is { } packaged && File.Exists(packaged)
            ? packaged
            : null;
    }

    private static bool Under(string path, string root) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs every stylesheet through esbuild in one pass and reads back what it produced.
    /// </summary>
    private IEnumerable<KeyValuePair<string, ProcessedStyle>> Compile(IReadOnlyDictionary<string, string> sources)
    {
        if (sources.Count == 0)
        {
            yield break;
        }

        if (esbuild is null)
        {
            // Scoping is the part that cannot be faked, so a project with CSS modules and no
            // esbuild is told rather than served stylesheets whose class names mean nothing.
            report?.Invoke(
                "JsxCore could not process the stylesheets views import, because esbuild was not " +
                "found. Build the project to restore it, or add it with `dotnet npm add esbuild --dev`.");

            yield break;
        }

        var staging = Path.Combine(_output, Directory, ".staging");
        var entries = new List<(string Specifier, string Entry, string Name)>();

        // Ordered, so the names esbuild disambiguates are the same from one build to the next.
        var ordered = sources.OrderBy(pair => pair.Value, StringComparer.Ordinal).ToList();

        System.IO.Directory.CreateDirectory(staging);

        for (var index = 0; index < ordered.Count; index++)
        {
            var (specifier, source) = ordered[index];
            var name = index.ToString();
            var entry = Path.Combine(staging, name + ".js");

            // An entry per stylesheet rather than one for all of them, so each keeps its own
            // output and a view links only what it actually imports.
            File.WriteAllText(entry, $"import s from {Quote(source)};\nexport default s;\n");
            entries.Add((specifier, entry, name));
        }

        if (!Run(entries.Select(entry => entry.Entry), staging))
        {
            yield break;
        }

        foreach (var (specifier, _, name) in entries)
        {
            var css = Path.Combine(staging, name + ".css");
            if (!File.Exists(css))
            {
                // A stylesheet that produced nothing, which an empty file does.
                continue;
            }

            var relative = UrlFor(specifier, sources[specifier]);
            var target = Path.Combine(_output, Directory, relative.Replace('/', Path.DirectorySeparatorChar));

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            AssetStage.WriteFileIfChanged(target, File.ReadAllBytes(css));

            var names = sources[specifier].EndsWith(ModuleSuffix, StringComparison.OrdinalIgnoreCase)
                ? ReadNames(Path.Combine(staging, name + ".js"))
                : null;

            yield return new KeyValuePair<string, ProcessedStyle>(
                specifier, new ProcessedStyle($"{Directory}/{relative}", Rooted: false, names));
        }

        Delete(staging);
    }

    /// <summary>
    /// Where a processed stylesheet lands, which is its path within whichever tree it came from.
    /// </summary>
    private string UrlFor(string specifier, string source) =>
        Under(source, _views)
            ? Path.GetRelativePath(_views, source).Replace(Path.DirectorySeparatorChar, '/')
            : "npm/" + specifier.TrimStart('.', '/');

    /// <summary>
    /// The scoped names esbuild generated, lifted out of the module it wrote them into.
    /// </summary>
    /// <remarks>
    /// Read as source rather than parsed. It is esbuild's own output, so the shape is known, and
    /// the object is copied into the generated module verbatim rather than rebuilt from a model
    /// that could disagree with it.
    /// </remarks>
    private static string? ReadNames(string emitted)
    {
        if (!File.Exists(emitted))
        {
            return null;
        }

        var source = File.ReadAllText(emitted);

        // The first brace opens the name map esbuild declared; matching it is what stops this
        // swallowing the "export { ... }" that follows, which would be a second default export.
        var open = source.IndexOf('{');
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            depth += source[index] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(index + 1)];
            }
        }

        return null;
    }

    private bool Run(IEnumerable<string> entries, string outputDirectory)
    {
        var startInfo = new ProcessStartInfo(esbuild!.ExecutablePath)
        {
            WorkingDirectory = outputDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var entry in entries)
        {
            startInfo.ArgumentList.Add(entry);
        }

        startInfo.ArgumentList.Add("--bundle");
        startInfo.ArgumentList.Add($"--loader:{ModuleSuffix}=local-css");
        startInfo.ArgumentList.Add($"--outdir={outputDirectory}");
        startInfo.ArgumentList.Add($"--outbase={outputDirectory}");
        startInfo.ArgumentList.Add("--format=esm");
        startInfo.ArgumentList.Add("--log-level=warning");

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds))
            {
                report?.Invoke("JsxCore gave up waiting for esbuild while processing stylesheets.");
                return false;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            report?.Invoke($"JsxCore could not process stylesheets with esbuild:{Environment.NewLine}{error}");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception)
        {
            report?.Invoke($"JsxCore could not run esbuild to process stylesheets: {exception.Message}");
            return false;
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Staging left behind is rewritten by the next build.
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
