namespace JsxCore.Compilation.Assets;

/// <summary>
/// Preact, shipped inside the JsxCore package.
/// </summary>
/// <remarks>
/// Requiring it from npm would mean every application needed a restore and a network connection
/// before it could render anything. Preact is small and MIT licensed, so it travels with JsxCore
/// instead. A copy in node_modules still wins, so upgrading does not wait for a JsxCore release.
/// </remarks>
public static class VendoredPreact
{
    private const string ResourcePrefix = "JsxCore.Assets.vendor.";

    /// <summary>
    /// The versions copied in, part of the build id so an upgrade moves every asset URL. Kept in
    /// step with Assets/vendor/preact/README.md by hand, as the copying is.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Versions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["preact"] = "10.29.7",
        ["preact-render-to-string"] = "6.7.0"
    };

    /// <summary>
    /// Type declarations, keyed by where they have to land on disk: they import each other
    /// relatively, so they only resolve once npm's directory structure is recreated.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TypeFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["preact/src/index.d.ts"] = "preact_types.preact.src.index.d.ts",
        ["preact/src/jsx.d.ts"] = "preact_types.preact.src.jsx.d.ts",
        ["preact/src/dom.d.ts"] = "preact_types.preact.src.dom.d.ts",
        ["preact/hooks/src/index.d.ts"] = "preact_types.preact.hooks.src.index.d.ts",
        ["preact/jsx-runtime/src/index.d.ts"] = "preact_types.preact.jsx_runtime.src.index.d.ts",
        ["preact/compat/src/index.d.ts"] = "preact_types.preact.compat.src.index.d.ts",
        ["preact/compat/src/suspense.d.ts"] = "preact_types.preact.compat.src.suspense.d.ts",
        ["preact/compat/src/suspense-list.d.ts"] = "preact_types.preact.compat.src.suspense-list.d.ts",
        ["preact-render-to-string/dist/index.d.ts"] = "preact_types.preact_render_to_string.dist.index.d.ts"
    };

    /// <summary>Writes the type declarations under <paramref name="directory"/>, npm's layout intact.</summary>
    public static void StageTypes(string directory)
    {
        foreach (var (relativePath, resource) in TypeFiles)
        {
            if (Read(resource) is not { } content)
            {
                continue;
            }

            var path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AssetStage.WriteFileIfChanged(path, content);
        }
    }

    /// <summary>The vendored runtime module, or null when JsxCore does not ship that one.</summary>
    public static byte[]? ReadModule(string fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : Read("preact." + fileName);

    public static bool Has(string fileName) => ReadModule(fileName) is not null;

    /// <summary>An embedded vendored file, named relative to the vendor directory.</summary>
    public static byte[]? Read(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        using var stream = typeof(VendoredPreact).Assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
