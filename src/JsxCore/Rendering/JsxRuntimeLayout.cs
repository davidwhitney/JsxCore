using JsxCore.TypeScript;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;

namespace JsxCore.Rendering;

public sealed class JsxRuntimeLayout
{
    private JsxRuntimeLayout() { }

    /// <summary>The framework this layout serves, as the project file names it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The specifier the generated document imports <c>mountView</c> from.
    /// </summary>
    /// <remarks>
    /// Framework-neutral, so the page a browser receives reads the same whichever framework is
    /// serving it, and a document template that emits this is not writing a framework's name into
    /// markup. The framework-specific names remain in the import map and resolve to the same file.
    /// </remarks>
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

    /// <summary>
    /// Bare specifiers this framework's entry points need from npm. The import map is built by
    /// walking compiled views, which never mention these: JsxCore's own entries import them.
    /// </summary>
    public IReadOnlyList<string> ClientDependencies { get; private init; } = [];
    public required string AssetSegment { get; init; }

    /// <summary>
    /// React, resolved out of node_modules like any other package: nothing of it is mapped here,
    /// so its import map entries come from the npm pipeline rather than from this layout.
    /// </summary>
    public static JsxRuntimeLayout React(ReactEntryStager stager)
    {
        ArgumentNullException.ThrowIfNull(stager);

        return new JsxRuntimeLayout
        {
            Name = "react",
            ClientSpecifier = RuntimeAssets.ClientSpecifier,
            ServerEntrySpecifier = "@jsxcore/react/server",
            AssetSegment = "react",
            Directory = stager.Directory,
            ClientDependencies = ["react", "react-dom/client"],
            Modules = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["@jsxcore/react/client"] = "client.js",
                ["@jsxcore/react/server"] = "server.js",

                // Same file under a name that does not say which framework, so mounting code
                // survives a change of <JsxCoreFramework>.
                [RuntimeAssets.ClientSpecifier] = "client.js"
            }
        };
    }

    public static JsxRuntimeLayout Preact(PreactVendorStager stager, bool reactCompatibility)
    {
        ArgumentNullException.ThrowIfNull(stager);

        return new JsxRuntimeLayout
        {
            Name = "preact",
            ClientSpecifier = RuntimeAssets.ClientSpecifier,
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

        // Same file under a name that does not say which framework, so mounting code survives a
        // change of <JsxCoreFramework>.
        modules[RuntimeAssets.ClientSpecifier] = "client.js";

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

        // The .NET interop helpers belong to JsxCore rather than to any framework, so they are
        // served from its own runtime directory whatever renders the view.
        //
        // An exact entry, and no trailing-slash one: a prefix mapping has to end in "/" to be a
        // prefix, and "dotnet:" does not. Chrome, Firefox and WebKit all decline to map "dotnet:/",
        // consistently, so every specifier this scheme serves is listed rather than implied.
        //
        // index.js, which is what the server-side loader resolves the same specifier to. Naming a
        // different module here would hand the browser a different set of exports from the one the
        // declarations describe and the server renderer sees.
        map[RuntimeAssets.ModuleSpecifier] = $"{assetBase}/runtime/index.js";

        // Listed rather than implied, for the same reason: a sub-path of the scheme is still a
        // specifier the browser has to be told about one at a time.
        foreach (var name in RuntimeAssets.PublicSubModules)
        {
            map[$"{RuntimeAssets.ModuleSpecifier}/{name}"] = $"{assetBase}/runtime/{name}.js";
        }

        // Generated from what the application registered, and served from the same directory.
        map[TypeDefinitionOptions.GlobalsSpecifier] =
            $"{assetBase}/runtime/{GeneratedGlobalsModule.FileName}";

        return map;
    }
}
