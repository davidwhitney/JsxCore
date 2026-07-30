namespace JsxCore.Compilation.Assets;

/// <summary>
/// Preact, shipped inside the JsxCore package.
/// </summary>
/// <remarks>
/// <para>
/// The runtime a view compiles against has to exist before anything can render, so requiring it
/// from npm would make "install the package and write a view" untrue: every application would need
/// a restore, a network connection on its first build, and two more directories in its publish
/// output. Preact is small and MIT licensed, so it travels with JsxCore instead.
/// </para>
/// <para>
/// A copy in node_modules still wins, which is what keeps upgrading independently possible: install
/// a newer Preact and it is used, without waiting for a JsxCore release.
/// </para>
/// </remarks>
public static class VendoredPreact
{
    private const string ResourcePrefix = "JsxCore.Assets.vendor.";

    /// <summary>
    /// The versions copied in, which are part of the build id so an upgrade moves every asset URL.
    /// </summary>
    /// <remarks>Kept in step with Assets/vendor/preact/README.md by hand, as the copying is.</remarks>
    public static IReadOnlyDictionary<string, string> Versions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["preact"] = "10.29.7",
        ["preact-render-to-string"] = "6.7.0"
    };

    /// <summary>
    /// Preact's own type declarations, keyed by where they have to land on disk.
    /// </summary>
    /// <remarks>
    /// The paths matter: these files import each other relatively, so they only resolve once the
    /// directory structure npm would have produced is recreated. The resource names are what
    /// MSBuild made of those paths, which is not something worth deriving twice.
    /// </remarks>
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
