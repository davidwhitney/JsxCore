using System.Text.Json;

namespace JsxCore.Compilation.Modules;

public sealed record DeclaredPackage(string Name, string Range, bool Development);

public sealed class PackageManifest
{
    private IReadOnlyList<DeclaredPackage>? _packages;

    private PackageManifest(string path, JsonElement root)
    {
        Path = path;
        Root = root;
    }

    public string Path { get; }

    public JsonElement Root { get; }

    // Worked out on first use: resolution touches a manifest for every package it walks through
    // and has no interest in what they declare.
    public IReadOnlyList<DeclaredPackage> Packages => _packages ??= ReadPackages();

    public IEnumerable<DeclaredPackage> Dependencies => Packages.Where(p => !p.Development);
    public IEnumerable<DeclaredPackage> DevDependencies => Packages.Where(p => p.Development);

    public IReadOnlySet<string> RuntimeNames =>
        Dependencies.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    public string Type =>
        Root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? "commonjs"
            : "commonjs";

    public bool TryGetExports(out JsonElement exports) =>
        Root.TryGetProperty("exports", out exports);

    public string? Field(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static PackageManifest? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            // Cloned so the element outlives the document it came from.
            // Cloned so the element outlives the document it came from.
            return new PackageManifest(System.IO.Path.GetFullPath(path), document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static PackageManifest? In(string directory) =>
        Read(System.IO.Path.Combine(directory, "package.json"));

    public static PackageManifest? Nearest(string startDirectory)
    {
        var directory = new DirectoryInfo(System.IO.Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (In(directory.FullName) is { } manifest)
            {
                return manifest;
            }
            directory = directory.Parent;
        }

        return null;
    }

    private IReadOnlyList<DeclaredPackage> ReadPackages()
    {
        var packages = new List<DeclaredPackage>();
        Collect("dependencies", development: false, packages);
        Collect("devDependencies", development: true, packages);
        return packages;
    }

    private void Collect(string property, bool development, List<DeclaredPackage> into)
    {
        if (!Root.TryGetProperty(property, out var block) || block.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var entry in block.EnumerateObject())
        {
            into.Add(new DeclaredPackage(
                entry.Name,
                entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? string.Empty : string.Empty,
                development));
        }
    }
}
