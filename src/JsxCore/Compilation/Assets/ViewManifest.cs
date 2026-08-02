using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// What the linker learned about one compiled module: the modules it imports, the URLs of the
/// stylesheets it pulls in, and the render mode its directive prologue asks for.
/// </summary>
public sealed record ViewModule(
    IReadOnlyList<string> Imports,
    IReadOnlyList<string> Styles,
    RenderMode? Mode = null);

/// <summary>
/// The part of the compiled module graph the renderer needs, recorded once per build.
/// </summary>
/// <remarks>
/// <para>
/// Two questions are answered here. The server has to ask both before it renders, and it can
/// answer neither from the module itself.
/// </para>
/// <para>
/// <b>Which stylesheets a view brings with it.</b> A stylesheet import has no runtime value to
/// return, since <c>import "dotnet:wwwroot/app.css"</c> binds nothing, so unlike an image it cannot
/// be answered by a module that exports a URL. The document has to carry a link element. Recorded
/// from the emitted output and walked depth first from the view being rendered, so the order is the
/// one the import graph implies: CSS is order dependent in a way ES modules are not, and two views
/// importing the same pair of stylesheets must not produce different cascades because one of them
/// rendered first.
/// </para>
/// <para>
/// <b>Where a view wants to run.</b> A <c>"use client"</c> or <c>"use server"</c> directive is a
/// property of the source, and the server picks a render mode before any JavaScript runs. Reading
/// it here rather than from the <c>.tsx</c> costs nothing per request and works on a server
/// published with no sources on it.
/// </para>
/// </remarks>
public sealed class ViewManifest
{
    public static readonly ViewManifest Empty = new();

    /// <summary>Keyed by module path within the compiled output, forward-slashed.</summary>
    public Dictionary<string, ViewModule> Modules { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether anything here is worth writing to disk.</summary>
    [JsonIgnore]
    public bool HasContent =>
        Modules.Values.Any(module => module.Styles.Count > 0 || module.Mode is not null);

    /// <summary>
    /// The render mode a module's directive prologue asks for, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Only the module being rendered is consulted. A directive on a component it imports says
    /// nothing about the response: the mode is chosen once, for the view the endpoint named, and a
    /// shared component that quietly changed how every page importing it rendered would be a trap
    /// rather than a feature.
    /// </remarks>
    public RenderMode? ModeFor(string modulePath) =>
        string.IsNullOrEmpty(modulePath) || Modules.Count == 0
            ? null
            : Modules.GetValueOrDefault(Normalise(modulePath))?.Mode;

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

    public static ViewManifest Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ViewManifest>(json, SerializerOptions) ?? Empty;
        }
        catch (JsonException)
        {
            // A manifest from a half-written build costs a page its stylesheets, not its response.
            return Empty;
        }
    }

    /// <summary>Reads the manifest written beside the compiled views, if there is one.</summary>
    public static ViewManifest ReadFrom(string outputDirectory)
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
    // reflection-based serialisation is not on by default in either. Modes are written as names
    // because this file is meant to be readable, and "Server" survives a reordered enum where 1
    // does not.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() }
    };
}
