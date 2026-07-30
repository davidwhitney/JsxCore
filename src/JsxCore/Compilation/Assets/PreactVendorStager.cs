using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Assets;

public sealed class PreactVendorStager(
    CompilationLayout layout,
    NodeModulesLayout nodeModules,
    ILogger logger)
{
    public static readonly IReadOnlyList<PreactModule> Modules =
    [
        new("preact", "preact/dist/preact.mjs", "preact.js", Required: true),
        new("preact/hooks", "preact/hooks/dist/hooks.mjs", "hooks.js", Required: true),
        new("preact/jsx-runtime", "preact/jsx-runtime/dist/jsxRuntime.mjs", "jsx-runtime.js", Required: true),
        new("preact/jsx-dev-runtime", "preact/jsx-runtime/dist/jsxRuntime.mjs", "jsx-dev-runtime.js", Required: false),
        new("preact-render-to-string", "preact-render-to-string/dist/index.mjs", "render-to-string.js", Required: true),
        new("preact/compat", "preact/compat/dist/compat.mjs", "compat.js", Required: false),
        new("preact/debug", "preact/debug/dist/debug.mjs", "debug.js", Required: false),
        new("preact/devtools", "preact/devtools/dist/devtools.mjs", "devtools.js", Required: false)
    ];

    public static readonly string[] VersionedPackages = ["preact", "preact-render-to-string"];

    private readonly CompilationLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly NodeModulesLayout _nodeModules = nodeModules ?? throw new ArgumentNullException(nameof(nodeModules));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string Directory => Path.Combine(_layout.WorkingDirectory, "preact");
    public IReadOnlyDictionary<string, string> Staged => _staged;
    private readonly Dictionary<string, string> _staged = new(StringComparer.Ordinal);

    public string Stage()
    {
        _staged.Clear();
        var result = AssetStage.WriteTo(Directory, new VendoredPreactModules(this));

        // What was actually written, rather than what node_modules happens to contain: a module
        // JsxCore ships is staged too, and leaving it out of this map is what makes server
        // rendering fall through to node_modules and report Preact as missing.
        foreach (var module in Modules.Where(m =>
                     ResolveInNodeModules(m.PackagePath) is not null || VendoredPreact.Has(m.FileName)))
        {
            _staged[module.Specifier] = module.FileName;
        }

        return result.Fingerprint;
    }

    public string MissingModuleMessage(PreactModule module) =>
        $"JsxCore is configured to use Preact, but '{module.PackagePath}' was not found in node_modules." +
        $"{Environment.NewLine}{Environment.NewLine}" +
        $"Install it with:{Environment.NewLine}{Environment.NewLine}" +
        $"    npm install preact preact-render-to-string{Environment.NewLine}{Environment.NewLine}" +
        $"Searched: {string.Join(", ", _nodeModules.Roots)}";

    public void LogOptionalModuleMissing(PreactModule module) =>
        _logger.LogDebug("JsxCore: optional Preact module {Specifier} is not installed.", module.Specifier);

    public string? ResolveInNodeModules(string relativePath) => _nodeModules.FindFile(relativePath);

    public string? ReadInstalledVersion(string packageName) =>
        ResolveInNodeModules($"{packageName}/package.json") is { } path
            ? PackageManifest.Read(path)?.Field("version")
            : null;
}
