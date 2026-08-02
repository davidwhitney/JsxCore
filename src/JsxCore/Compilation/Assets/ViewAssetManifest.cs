using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// One compiled module: which modules it imports, and the URLs of the stylesheets it pulls in.
/// </summary>
public sealed record ViewAssetModule(IReadOnlyList<string> Imports, IReadOnlyList<string> Styles);

/// <summary>
/// The part of the compiled module graph the renderer needs: which stylesheets a view brings with
/// it, and in which order.
/// </summary>
/// <remarks>
/// <para>
/// A stylesheet import has no runtime value to return — <c>import "dotnet:wwwroot/app.css"</c>
/// binds nothing — so unlike an image it cannot be answered by a module that exports a URL. The
/// document has to carry a link element, and the document is written by the server, which is why
/// what a view imports has to be recorded somewhere the server can read.
/// </para>
/// <para>
/// Recorded from the emitted output rather than from the sources, and walked depth first from the
/// view being rendered, so the order stylesheets appear in is the order the import graph implies.
/// CSS is order dependent in a way ES modules are not: two views importing the same pair of
/// stylesheets must not produce different cascades because one of them rendered first.
/// </para>
/// </remarks>
public sealed class ViewAssetManifest
{
    public static readonly ViewAssetManifest Empty = new();

    /// <summary>Keyed by module path within the compiled output, forward-slashed.</summary>
    public Dictionary<string, ViewAssetModule> Modules { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether any module in the graph imports a stylesheet.</summary>
    public bool HasStyles => Modules.Values.Any(module => module.Styles.Count > 0);

    /// <summary>
    /// Stylesheets a module brings with it, dependencies first, each appearing once.
    /// </summary>
    public IReadOnlyList<string> StylesFor(string modulePath)
    {
        if (string.IsNullOrEmpty(modulePath) || Modules.Count == 0)
        {
            return [];
        }

        var styles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Visit(Normalise(modulePath), styles, seen, visited);
        return styles;
    }

    private void Visit(string modulePath, List<string> styles, HashSet<string> seen, HashSet<string> visited)
    {
        // A cycle in the module graph is legal JavaScript, so this stops rather than fails.
        if (!visited.Add(modulePath) || !Modules.TryGetValue(modulePath, out var module))
        {
            return;
        }

        // Depth first, and the module's own stylesheets last: a component's styles come before the
        // page's, so the page can override them.
        foreach (var import in module.Imports)
        {
            Visit(import, styles, seen, visited);
        }

        foreach (var style in module.Styles.Where(style => seen.Add(style)))
        {
            styles.Add(style);
        }
    }

    private static string Normalise(string path) => path.Replace('\\', '/').TrimStart('/');

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ViewAssetManifest Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ViewAssetManifest>(json, SerializerOptions) ?? Empty;
        }
        catch (JsonException)
        {
            // A manifest from a half-written build costs a page its stylesheets, not its response.
            return Empty;
        }
    }

    /// <summary>Reads the manifest written beside the compiled views, if there is one.</summary>
    public static ViewAssetManifest ReadFrom(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, ViewAssets.ManifestFileName);

        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
    }

    // Stated rather than inherited: the build tool and a trimmed application both run this, and
    // reflection-based serialisation is not on by default in either.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
