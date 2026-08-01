using JsxCore.TypeScript;
using Jint;
using Jint.Runtime.Modules;
using JsxCore.Compilation;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Assets;

namespace JsxCore.Rendering;

/// <summary>
/// Resolves ES module specifiers for the server-side renderer.
/// </summary>
/// <remarks>
/// <para>
/// Views resolve against the same compiled output the browser loads, so a component renders from
/// identical bytes on both sides. The <c>dotnet:rendering</c> family resolves to paths under the
/// runtime directory, but those files are never read from disk: <see cref="LoadModule"/> serves
/// their source from resources embedded in this assembly, which keeps the runtime out of the
/// consuming project. Using real paths rather than a synthetic URI scheme matters because the
/// engine echoes a module's own location back when resolving its relative imports.
/// </para>
/// <para>
/// Any other bare specifier is a package name, resolved against node_modules when
/// <see cref="JsxCoreOptions.AllowNodeModules"/> is on and rejected with an explanatory
/// message when it is off or the package is not installed.
/// </para>
/// </remarks>
public sealed class JsxModuleLoader : IModuleLoader
{
    private readonly string _outputDirectory;
    private readonly string _runtimeDirectory;
    private readonly JsxRuntimeLayout _runtime;
    private readonly string? _runtimeAssetDirectory;
    private readonly NodeModuleResolver? _npm;

    public JsxModuleLoader(CompilationLayout layout, JsxRuntimeLayout runtime, NodeModuleResolver? npm = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _npm = npm;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _outputDirectory = Path.GetFullPath(layout.OutputDirectory);
        _runtimeDirectory = Path.GetFullPath(layout.RuntimeDirectory);
        _runtimeAssetDirectory = runtime.Directory is null ? null : Path.GetFullPath(runtime.Directory);
    }

    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var specifier = moduleRequest.Specifier;

        // A runtime that lives on disk (Preact staged out of node_modules) owns its specifiers.
        if (_runtimeAssetDirectory is not null && _runtime.ResolveModule(specifier) is { } runtimeFile)
        {
            return Resolved(moduleRequest, Path.Combine(_runtimeAssetDirectory, runtimeFile), SpecifierType.Bare);
        }

        // Generated from the application's registrations, so it lives on disk rather than in the
        // assembly manifest like the rest of the runtime.
        if (specifier == TypeDefinitionOptions.GlobalsSpecifier)
        {
            return Resolved(
                moduleRequest,
                Path.Combine(_runtimeDirectory, GeneratedGlobalsModule.FileName),
                SpecifierType.Bare);
        }

        // The built-in runtime is served from embedded resources rather than files.
        if (specifier.StartsWith(RuntimeAssets.ModuleSpecifier, StringComparison.Ordinal))
        {
            var sub = specifier.Length == RuntimeAssets.ModuleSpecifier.Length
                ? "index"
                : specifier[(RuntimeAssets.ModuleSpecifier.Length + 1)..];

            var runtimePath = Path.Combine(_runtimeDirectory, WithJsExtension(sub));
            return Resolved(moduleRequest, runtimePath, SpecifierType.Bare);
        }

        if (specifier.StartsWith('.') || Path.IsPathRooted(specifier))
        {
            var baseDirectory = string.IsNullOrEmpty(referencingModuleLocation)
                ? _outputDirectory
                : Path.GetDirectoryName(referencingModuleLocation) ?? _outputDirectory;

            var path = Path.GetFullPath(Path.Combine(baseDirectory, specifier.Replace('/', Path.DirectorySeparatorChar)));

            // A relative import from inside a package stays inside node_modules.
            if (_npm is not null && referencingModuleLocation is not null
                && _npm.IsInsideNodeModules(referencingModuleLocation))
            {
                var withinPackage = _npm.ResolveRelative(specifier, referencingModuleLocation)
                    ?? throw new JsxCoreException(
                        $"JsxCore could not resolve '{specifier}' from '{referencingModuleLocation}'.");

                return Resolved(moduleRequest, withinPackage.Path, SpecifierType.RelativeOrAbsolute);
            }

            EnsureWithinRoots(path, specifier);
            return Resolved(moduleRequest, path, SpecifierType.RelativeOrAbsolute);
        }

        if (_npm?.Resolve(specifier, referencingModuleLocation) is { } package)
        {
            return Resolved(moduleRequest, package.Path, SpecifierType.Bare);
        }

        throw new JsxCoreException(
            $"JsxCore could not resolve the module '{specifier}' during server rendering." +
            (_npm is null
                ? $" Loading packages from node_modules is switched off; set " +
                  $"options.AllowNodeModules to enable it."
                : $" It was not found in node_modules. Searched: " +
                  string.Join(", ", _npm.SearchRoots)));
    }

    private static ResolvedSpecifier Resolved(ModuleRequest request, string path, SpecifierType type) =>
        new(request, path, new Uri(path), type);

    private static string WithJsExtension(string name) =>
        name.EndsWith(".js", StringComparison.Ordinal) ? name : name + ".js";

    // Relative specifiers must not be able to climb out of the directories JsxCore owns.
    private void EnsureWithinRoots(string path, string specifier)
    {
        if (IsUnder(path, _outputDirectory)
            || IsUnder(path, _runtimeDirectory)
            || (_runtimeAssetDirectory is not null && IsUnder(path, _runtimeAssetDirectory)))
        {
            return;
        }

        throw new JsxCoreException(
            $"JsxCore refused to load '{specifier}' because it resolves outside the compiled view output.");
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
    {
        if (_npm is not null && _npm.IsInsideNodeModules(resolved.Key))
        {
            return LoadPackageModule(engine, resolved);
        }

        var source = ReadSource(resolved.Key);
        return ModuleFactory.BuildSourceTextModule(engine, resolved, source, new ModuleParsingOptions());
    }

    private string ReadSource(string path)
    {
        // Staged runtime files are real files; the built-in runtime is not.
        if (_runtimeAssetDirectory is not null && IsUnder(path, _runtimeAssetDirectory))
        {
            return File.Exists(path)
                ? File.ReadAllText(path)
                : throw new JsxCoreException($"JsxCore could not find the staged runtime module '{path}'.");
        }

        if (IsUnder(path, _runtimeDirectory))
        {
            var fileName = Path.GetRelativePath(_runtimeDirectory, path).Replace(Path.DirectorySeparatorChar, '/');

            // Embedded first, then disk: everything JsxCore ships is in the manifest, and what the
            // application's own registrations generate is written beside it.
            return RuntimeAssets.TryGetText(fileName)
                ?? (File.Exists(path) ? File.ReadAllText(path) : null)
                ?? throw new JsxCoreException($"The JsxCore runtime does not contain a module named '{fileName}'.");
        }

        if (!File.Exists(path))
        {
            throw new JsxCoreException(
                $"JsxCore could not find the compiled module '{path}'. This usually means the view failed " +
                $"to compile, or that it imports a file which is not under the views directory.");
        }

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Loads a file from node_modules as whatever it actually is, rather than assuming ESM.
    /// </summary>
    private Module LoadPackageModule(Engine engine, ResolvedSpecifier resolved)
    {
        var path = resolved.Key;
        if (!File.Exists(path))
        {
            throw new JsxCoreException($"JsxCore could not read the package module '{path}'.");
        }

        var source = File.ReadAllText(path);
        var kind = _npm!.KindOf(path);

        // JSON keeps the engine's own module type rather than being re-expressed as
        // "export default <json>". The two are not equivalent, and not in a harmless direction:
        // the rewrite is parsed as JavaScript, so it accepts comments, single quotes and unquoted
        // keys, none of which are JSON. A package carrying one of those would render on the server
        // and fail in the browser. A browser has no JSON module type and does need the rewrite,
        // which is why the two hosts differ here, and only here.
        if (kind == NodeModuleKind.Json)
        {
            return ModuleFactory.BuildJsonModule(engine, resolved, source);
        }

        var shaped = ModuleTransform.Apply(path, kind, source, new EngineSpecifierRewriter(_npm));
        return ModuleFactory.BuildSourceTextModule(engine, resolved, shaped.Source, new ModuleParsingOptions());
    }

}
