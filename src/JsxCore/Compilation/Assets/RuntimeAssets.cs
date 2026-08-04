using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace JsxCore.Compilation.Assets;

public static class RuntimeAssets
{
    private const string ResourcePrefix = "JsxCore.Assets.runtime.";
    /// <summary>
    /// Rendering a view: which pass is running, what the view receives, and what it may contribute
    /// to the document.
    /// </summary>
    /// <remarks>
    /// A reserved name under the same scheme as the assemblies, rather than a scheme of its own.
    /// One rule to hold: <c>dotnet:</c> is the .NET side of the application, meaning an assembly by
    /// name, or one of the names JsxCore reserves: <c>globals</c>, <c>rendering</c>, and <c>wwwroot/</c>
    /// followed by a path, which is the application's own served files.
    /// <para>
    /// It is the contract with the .NET host that lives here, even though JavaScript implements it:
    /// the server decides what a view is handed and which pass is running.
    /// </para>
    /// </remarks>
    public const string ModuleSpecifier = "dotnet:rendering";

    /// <summary>
    /// Modules of the built-in runtime a view can reach, each of which must be embedded.
    /// </summary>
    /// <remarks>
    /// Short, because rendering is Preact's or React's job. What used to sit alongside these was a
    /// second renderer with its own JSX factory and hooks, which nothing loaded: a view importing
    /// <c>useState</c> from here type checked and then failed at run time.
    /// </remarks>
    public static readonly IReadOnlyList<string> PublicModules = ["index", "dotnet"];

    /// <summary>
    /// Sub-paths of <see cref="ModuleSpecifier"/> a view may import, as in
    /// <c>dotnet:rendering/head</c>.
    /// </summary>
    /// <remarks>
    /// One list, read by the import map the browser gets and by the compiler configuration, so a
    /// specifier that type checks is one the browser can also resolve.
    /// </remarks>
    public static readonly IReadOnlyList<string> PublicSubModules = ["head"];

    /// <summary>
    /// The framework-neutral name for JsxCore's client entry point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolves to <c>@jsxcore/preact/client</c> or <c>@jsxcore/react/client</c>, whichever the
    /// project builds against, so code that mounts a view survives changing
    /// <c>&lt;JsxCoreFramework&gt;</c>. The declarations live in this directory; the module does
    /// not, because it belongs to the framework being served.
    /// </para>
    /// <para>
    /// Under <c>@jsxcore/</c> rather than <c>dotnet:</c> deliberately. <c>dotnet:</c> is the .NET
    /// side of the application, and mounting a component in a browser is not that.
    /// </para>
    /// </remarks>
    public const string ClientSpecifier = "@jsxcore/client";

    /// <summary>The declarations behind <see cref="ClientSpecifier"/>, in this directory.</summary>
    public const string ClientDeclarations = "client";

    private const string SharedResourcePrefix = "JsxCore.Assets.shared.";
    private const string PreactResourcePrefix = "JsxCore.Assets.preact.";
    private const string ReactResourcePrefix = "JsxCore.Assets.react.";
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyList<string>> PreactFileNames = new(() =>
        typeof(RuntimeAssets).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(PreactResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[PreactResourcePrefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    public static IReadOnlyList<string> PreactSourceFiles => PreactFileNames.Value;

    private static readonly Lazy<IReadOnlyList<string>> ReactFileNames = new(() =>
        typeof(RuntimeAssets).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ReactResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ReactResourcePrefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    /// <summary>JsxCore's own mount and render entry points for React.</summary>
    public static IReadOnlyList<string> ReactSourceFiles => ReactFileNames.Value;

    public static string? TryGetReactSource(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !ReactSourceFiles.Contains(fileName))
        {
            return null;
        }

        using var stream = typeof(RuntimeAssets).Assembly.GetManifestResourceStream(ReactResourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static readonly Lazy<IReadOnlyList<string>> SharedFileNames = new(() =>
        typeof(RuntimeAssets).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(SharedResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[SharedResourcePrefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// The parts of an entry point that are the same whatever renders the view, staged beside every
    /// framework's own entries so they can import them relatively.
    /// </summary>
    /// <remarks>
    /// One source file, staged into each framework directory. Serving a single copy from somewhere
    /// central would mean teaching both the import map and the module loader about it, for a file
    /// measured in single-digit kilobytes.
    /// </remarks>
    public static IEnumerable<StagedFile> SharedEntryFiles()
    {
        foreach (var fileName in SharedFileNames.Value)
        {
            using var stream = typeof(RuntimeAssets).Assembly.GetManifestResourceStream(SharedResourcePrefix + fileName)
                ?? throw new JsxCoreException($"Embedded shared entry source '{fileName}' is missing.");

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            yield return new StagedFile(fileName, buffer.ToArray());
        }
    }

    public static IReadOnlyList<string> AllFileNames => AllFiles;

    public static string? TryGetPreactSource(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !PreactSourceFiles.Contains(fileName))
        {
            return null;
        }

        using var stream = typeof(RuntimeAssets).Assembly.GetManifestResourceStream(PreactResourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static readonly Lazy<IReadOnlyList<string>> FileNames = new(() =>
        typeof(RuntimeAssets).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    public static IReadOnlyList<string> AllFiles => FileNames.Value;

    /// <summary>
    /// Globals evaluated into a server rendering engine before any module loads.
    /// </summary>
    /// <remarks>
    /// Packages built for a browser or for Node reach for these while their own module body runs,
    /// so providing them afterwards is too late.
    /// </remarks>
    public static string HostShims => TryGetText("host-shims.js")
        ?? throw new JsxCoreException("Embedded runtime resource 'host-shims.js' could not be opened.");
    
    public static byte[]? TryGetContent(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !AllFiles.Contains(fileName))
        {
            return null;
        }

        return Cache.GetOrAdd(fileName, static name =>
        {
            using var stream = typeof(RuntimeAssets).Assembly.GetManifestResourceStream(ResourcePrefix + name)
                ?? throw new JsxCoreException($"Embedded runtime resource '{name}' could not be opened.");

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    public static string? TryGetText(string fileName)
    {
        var content = TryGetContent(fileName);
        return content is null ? null : Encoding.UTF8.GetString(content);
    }

    public static string ExtractTypeDefinitions(string targetDirectory) =>
        AssetStage.WriteTo(targetDirectory, new RuntimeTypeDefinitions()).Fingerprint;

}
