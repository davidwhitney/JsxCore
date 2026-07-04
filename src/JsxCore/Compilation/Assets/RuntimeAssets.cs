using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace JsxCore.Compilation.Assets;

public static class RuntimeAssets
{
    private const string ResourcePrefix = "JsxCore.Assets.runtime.";
    public const string ModuleSpecifier = "@jsxcore/runtime";

    public static readonly IReadOnlyList<string> PublicModules =
    [
        "index", "jsx-runtime", "jsx-dev-runtime", "client", "server", "hooks", "dotnet", "dom"
    ];

    private const string PreactResourcePrefix = "JsxCore.Assets.preact.";
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyList<string>> PreactFileNames = new(() =>
        typeof(RuntimeAssets).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(PreactResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[PreactResourcePrefix.Length..])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    public static IReadOnlyList<string> PreactSourceFiles => PreactFileNames.Value;

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
