using JsxCore.Compilation;
using JsxCore.Compilation.Assets;

namespace JsxCore.Rendering;

public sealed class JsxRuntimeLayout
{
    private JsxRuntimeLayout() { }

    public required string ClientSpecifier { get; init; }

    public required string ServerEntrySpecifier { get; init; }

    private Func<IReadOnlyDictionary<string, string>> _modules = () =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Bare specifiers the runtime owns, mapped to a file name within its asset directory.
    /// Used by both the import map and the server-side module loader.
    /// </summary>
    /// <remarks>
    /// Resolved on each access rather than captured, because the Preact layout is constructed from
    /// the service container but its module set is only known once staging has run during startup.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Modules
    {
        get => _modules();
        init => _modules = () => value;
    }

    public string? Directory { get; private init; }
    public required string AssetSegment { get; init; }

    public static JsxRuntimeLayout Preact(PreactVendorStager stager, bool reactCompatibility)
    {
        ArgumentNullException.ThrowIfNull(stager);

        return new JsxRuntimeLayout
        {
            ClientSpecifier = "@jsxcore/preact/client",
            ServerEntrySpecifier = "@jsxcore/preact/server",
            AssetSegment = "preact",
            Directory = stager.Directory,
            _modules = () => BuildPreactModules(stager, reactCompatibility)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildPreactModules(
        PreactVendorStager stager,
        bool reactCompatibility)
    {
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (specifier, fileName) in stager.Staged)
        {
            modules[specifier] = fileName;
        }

        modules["@jsxcore/preact/client"] = "client.js";
        modules["@jsxcore/preact/server"] = "server.js";

        // preact/compat is the React-compatible surface. Mapping the React specifiers onto it means
        // components written against React resolve without anyone editing their imports.
        if (reactCompatibility && stager.Staged.ContainsKey("preact/compat"))
        {
            modules["react"] = "compat.js";
            modules["react-dom"] = "compat.js";
            modules["react-dom/client"] = "compat.js";
            modules["react/jsx-runtime"] = "jsx-runtime.js";
            modules["react/jsx-dev-runtime"] = "jsx-runtime.js";
        }

        return modules;
    }

    public string? ResolveModule(string specifier) => Modules.GetValueOrDefault(specifier);

    public IReadOnlyDictionary<string, string> BuildImportMap(string assetBase)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (specifier, fileName) in Modules)
        {
            map[specifier] = $"{assetBase}/{AssetSegment}/{fileName}";
        }

        // Trailing-slash entry so anything not listed still resolves within the runtime directory.
        map[$"{RuntimeAssets.ModuleSpecifier}/"] = $"{assetBase}/runtime/";

        // The .NET interop helpers belong to JsxCore rather than to any framework, so they are
        // served from its own runtime directory whatever renders the view.
        map[RuntimeAssets.ModuleSpecifier] = $"{assetBase}/runtime/dotnet.js";
        map[$"{RuntimeAssets.ModuleSpecifier}/dotnet"] = $"{assetBase}/runtime/dotnet.js";

        return map;
    }
}
