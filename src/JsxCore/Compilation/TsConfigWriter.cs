using System.Text.Json;
using System.Text.Json.Nodes;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Assets;
using JsxCore.TypeScript;

namespace JsxCore.Compilation;

public sealed record CompilationLayout(
    string ViewsDirectory,
    string WorkingDirectory,
    string RuntimeDirectory,
    string OutputDirectory,
    string TsConfigPath,
    string ContentRoot,
    string GeneratedTypesDirectory,
    string WebRoot)
{
    public static CompilationLayout Create(JsxCoreOptions options, string contentRoot)
    {
        var views = ContentRootPath.Resolve(options.ViewsDirectory, contentRoot);
        var working = ContentRootPath.Resolve(options.WorkingDirectory, contentRoot);

        return new CompilationLayout(
            views,
            working,
            Path.Combine(working, "runtime"),
            Path.Combine(working, "js"),
            Path.Combine(working, "tsconfig.json"),
            Path.GetFullPath(contentRoot),
            options.TypeDefinitions.OutputPath is { } configured
                ? ContentRootPath.Resolve(configured, contentRoot)
                : Path.Combine(working, "types"),
            ContentRootPath.Resolve(options.WebRootDirectory, contentRoot));
    }
}

public static class TsConfigWriter
{
    public const string BaseConfigResource = "JsxCore.Assets.tsconfig.base.json";

    public static JsonObject ReadBaseConfig()
    {
        using var stream = typeof(TsConfigWriter).Assembly.GetManifestResourceStream(BaseConfigResource)
            ?? throw new JsxCoreException($"Embedded resource '{BaseConfigResource}' could not be opened.");

        using var reader = new StreamReader(stream);

        // tsconfig files are JSONC; the base carries explanatory comments.
        return JsonNode.Parse(
                   reader.ReadToEnd(),
                   documentOptions: new JsonDocumentOptions
                   {
                       CommentHandling = JsonCommentHandling.Skip,
                       AllowTrailingCommas = true
                   }) as JsonObject
               ?? throw new JsxCoreException("The embedded tsconfig base is not a JSON object.");
    }

    public static JsonObject Build(
        JsxCoreOptions options,
        CompilationLayout layout,
        NodeModulesLayout? nodeModules = null,
        JsFramework framework = JsFramework.Preact)
    {
        var config = ReadBaseConfig();
        var compilerOptions = config["compilerOptions"]!.AsObject();

        ApplyRuntimeMode(options, compilerOptions, layout, layout.WorkingDirectory, nodeModules, framework);

        compilerOptions["rootDir"] = Normalise(layout.ViewsDirectory);
        compilerOptions["outDir"] = Normalise(layout.OutputDirectory);
        var paths = new JsonObject
        {
            [RuntimeAssets.ModuleSpecifier] = new JsonArray("./runtime/index.d.ts")
        };
        AddSubModulePaths(paths, "./runtime");
        paths[ViewsAlias] = new JsonArray(Normalise(layout.ViewsDirectory) + "/*");
        var ambientTypes = AddGeneratedTypesPath(options, layout, paths, relativeTo: layout.WorkingDirectory);
        compilerOptions["paths"] = MergePaths(compilerOptions, paths);

        if (options.TypeChecking == TypeCheckingMode.Off)
        {
            compilerOptions["noCheck"] = true;
        }

        ApplyUserOptions(compilerOptions, options);

        var includes = new JsonArray();
        foreach (var extension in options.Extensions.Concat([".ts", ".js"]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            includes.Add(Normalise(layout.ViewsDirectory) + "/**/*" + extension);
        }

        if (ambientTypes is not null)
        {
            includes.Add(ambientTypes);
        }

        includes.Add(WriteAssetDeclarations(layout, relativeTo: layout.WorkingDirectory));

        config["include"] = includes;
        return config;
    }

    private static void ApplyRuntimeMode(
        JsxCoreOptions options,
        JsonObject compilerOptions,
        CompilationLayout layout,
        string relativeTo,
        NodeModulesLayout? nodeModules = null,
        JsFramework framework = JsFramework.Preact)
    {
        // Built once when the caller has not already done so: this looks up nine declaration files
        // and each lookup used to walk for node_modules from scratch.
        nodeModules ??= NodeModulesLayout.For(layout.ContentRoot, options.AdditionalToolchainSearchPaths);

        compilerOptions.Remove("types");

        if (framework == JsFramework.React)
        {
            compilerOptions["jsxImportSource"] = "react";

            // React publishes no type declarations, so they come from DefinitelyTyped, which the
            // build installs. Mapped explicitly for the same reason Preact's are: this config lives
            // under obj/, and walking up from there for node_modules/@types finds nothing when the
            // content root is somewhere else.
            var reactPaths = new JsonObject();

            void MapReactType(string specifier, string declarationPath)
            {
                if (nodeModules.FindFile(declarationPath) is not { } found)
                {
                    return;
                }

                var path = Normalise(Path.GetRelativePath(relativeTo, found));
                reactPaths[specifier] = new JsonArray(
                    !path.StartsWith('.') && !Path.IsPathRooted(path) ? "./" + path : path);
            }

            MapReactType("react", "@types/react/index.d.ts");
            MapReactType("react/jsx-runtime", "@types/react/jsx-runtime.d.ts");
            MapReactType("react/jsx-dev-runtime", "@types/react/jsx-dev-runtime.d.ts");
            MapReactType("react-dom", "@types/react-dom/index.d.ts");
            MapReactType("react-dom/client", "@types/react-dom/client.d.ts");
            MapReactType("react-dom/server.browser", "@types/react-dom/server.browser.d.ts");

            compilerOptions["preactPaths"] = reactPaths;
            return;
        }

        // Preact ships its own type declarations, so the JSX namespace and hook types come
        // straight from the application's installed version.
        compilerOptions["jsxImportSource"] = "preact";

        // Map the Preact packages explicitly rather than relying on the compiler walking up to
        // node_modules. The generated config lives under obj/, and an application whose content
        // root sits outside the npm project would otherwise fail to resolve any Preact types.
        var paths = new JsonObject();

        // Preact's declarations are shipped inside JsxCore, so views type check with nothing
        // installed. Staged here rather than read from the assembly, because these files import
        // each other relatively and only resolve once npm's directory layout is recreated.
        var vendoredTypes = Path.Combine(layout.WorkingDirectory, "preact-types");
        VendoredPreact.StageTypes(vendoredTypes);

        void MapType(string specifier, string declarationPath)
        {
            // An installed copy wins, so a project that upgrades Preact type checks against the
            // version it will actually run.
            var resolved = nodeModules.FindFile(declarationPath)
                           ?? Existing(Path.Combine(vendoredTypes, declarationPath.Replace('/', Path.DirectorySeparatorChar)));

            if (resolved is null)
            {
                return;
            }

            // Relative, not absolute: the editor config is meant to be committed, and an absolute
            // node_modules path would only be correct on the machine that generated it.
            var relative = Normalise(Path.GetRelativePath(relativeTo, resolved));
            if (!relative.StartsWith('.') && !Path.IsPathRooted(relative))
            {
                relative = "./" + relative;
            }

            paths[specifier] = new JsonArray(relative);
        }

        MapType("preact", "preact/src/index.d.ts");
        MapType("preact/hooks", "preact/hooks/src/index.d.ts");
        MapType("preact/jsx-runtime", "preact/jsx-runtime/src/index.d.ts");
        MapType("preact/jsx-dev-runtime", "preact/jsx-runtime/src/index.d.ts");
        MapType("preact/compat", "preact/compat/src/index.d.ts");
        MapType("preact-render-to-string", "preact-render-to-string/dist/index.d.ts");

        if (options.EnableReactCompatibility)
        {
            // React-targeted imports type check through compat, matching how they resolve at runtime.
            MapType("react", "preact/compat/src/index.d.ts");
            MapType("react-dom", "preact/compat/src/index.d.ts");
            MapType("react-dom/client", "preact/compat/src/index.d.ts");
        }

        compilerOptions["preactPaths"] = paths;
    }

    public const string GeneratedMarker = "jsxcore-generated";
    
    public static JsonObject BuildIdeConfig(JsxCoreOptions options, CompilationLayout layout)
    {
        var config = ReadBaseConfig();
        var compilerOptions = config["compilerOptions"]!.AsObject();

        ApplyRuntimeMode(options, compilerOptions, layout, relativeTo: layout.ViewsDirectory);

        // Editors only type check; emit settings would just cause confusing output.
        compilerOptions["noEmit"] = true;
        compilerOptions.Remove("sourceMap");
        compilerOptions.Remove("inlineSources");
        compilerOptions.Remove("declaration");

        var runtime = Normalise(Path.GetRelativePath(layout.ViewsDirectory, layout.RuntimeDirectory));
        var paths = new JsonObject
        {
            [RuntimeAssets.ModuleSpecifier] = new JsonArray($"{runtime}/index.d.ts")
        };
        AddSubModulePaths(paths, runtime);
        paths[ViewsAlias] = new JsonArray("./*");

        // Called for the mapping and the stand-in it writes. The path it returns names that
        // stand-in, which the pattern below already covers.
        _ = AddGeneratedTypesPath(options, layout, paths, relativeTo: layout.ViewsDirectory);

        compilerOptions["paths"] = MergePaths(compilerOptions, paths);

        ApplyUserOptions(compilerOptions, options);

        config["//"] = $"{GeneratedMarker}: written by JsxCore so editors can resolve " +
                       $"'{RuntimeAssets.ModuleSpecifier}'. Delete or edit this file freely; JsxCore only " +
                       $"overwrites a file that still contains the marker above. Compilation itself uses " +
                       $"the config in the intermediate output directory, not this one.";
        // "**/*" is relative to the views directory, which the generated declarations sit outside.
        var includes = new JsonArray("**/*");

        // A mapping is enough for the compiler, which resolves once and exits. An editor caches
        // resolutions across builds and only watches files in the program, so the declarations are
        // listed rather than left to be found through a path.
        includes.Add(GeneratedTypesInclude(layout, relativeTo: layout.ViewsDirectory));

        includes.Add(WriteAssetDeclarations(layout, relativeTo: layout.ViewsDirectory));
        config["include"] = includes;

        return config;
    }
    
    public static bool WriteIdeConfigIfOwned(JsxCoreOptions options, CompilationLayout layout)
    {
        var path = Path.Combine(layout.ViewsDirectory, "tsconfig.json");

        if (File.Exists(path))
        {
            string existing;
            try
            {
                existing = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return false;
            }

            // Never clobber a hand-written config.
            if (!existing.Contains(GeneratedMarker, StringComparison.Ordinal))
            {
                return false;
            }

            var updated = BuildIdeConfig(options, layout).ToJsonString(Indented);
            if (existing == updated)
            {
                return false;
            }

            File.WriteAllText(path, updated);
            return true;
        }

        Directory.CreateDirectory(layout.ViewsDirectory);
        return AssetStage.WriteFileIfChanged(path, BuildIdeConfig(options, layout).ToJsonString(Indented));
    }

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // Stated rather than inherited from the host: serialising a node marks these options read
        // only, which fails where reflection-based serialisation is not enabled by default. The
        // build tool is such a host, and so is any trimmed or AOT-published application.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static bool WriteIfChanged(
        JsxCoreOptions options,
        CompilationLayout layout,
        NodeModulesLayout? nodeModules = null,
        JsFramework framework = JsFramework.Preact)
    {
        Directory.CreateDirectory(layout.WorkingDirectory);
        return AssetStage.WriteFileIfChanged(
            layout.TsConfigPath, Build(options, layout, nodeModules, framework).ToJsonString(Indented));
    }

    /// <summary>
    /// Points the compiler at the generated declarations, or at an ambient stand-in when they have
    /// not been generated yet. Returns a file the configuration has to include, if any.
    /// </summary>
    private static string? AddGeneratedTypesPath(
        JsxCoreOptions options,
        CompilationLayout layout,
        JsonObject paths,
        string relativeTo)
    {
        if (!options.TypeDefinitions.Enabled)
        {
            return null;
        }

        var relative = Normalise(Path.GetRelativePath(relativeTo, layout.GeneratedTypesDirectory));
        if (!relative.StartsWith('.') && !Path.IsPathRooted(relative))
        {
            relative = "./" + relative;
        }

        var globalsGenerated = File.Exists(
            Path.Combine(layout.GeneratedTypesDirectory, TypeDefinitionOptions.GlobalsFileName));

        // A path mapping to a file that is not there resolves to nothing, so the stand-in is
        // reached the other way: an ambient declaration in the program.
        //
        // The two are generated at different moments. A build can describe the models, because it
        // has the assembly, but never the globals, because registering one is application code the
        // build does not run. So the stand-in is needed whenever either is absent, not only on a
        // first build.
        if (!ModelTypeDeclarations.Exist(layout.GeneratedTypesDirectory) || !globalsGenerated)
        {
            var modelsGenerated = ModelTypeDeclarations.Exist(layout.GeneratedTypesDirectory);

            ModelTypeDeclarations.WritePending(
                layout.GeneratedTypesDirectory, models: !modelsGenerated, globals: !globalsGenerated);

            if (modelsGenerated)
            {
                // Models are there; only the globals need standing in for, so keep the mapping.
                paths[TypeDefinitionOptions.Scheme + "*"] = new JsonArray($"{relative}/*.d.ts");
            }

            return $"{relative}/{ModelTypeDeclarations.PendingFileName}";
        }

        // One wildcard rather than one entry per assembly: the name after "dotnet:" is the assembly's,
        // and the file generated for it is named to match, so the mapping needs no list to keep in
        // step with what was generated.
        paths[TypeDefinitionOptions.Scheme + "*"] = new JsonArray($"{relative}/*.d.ts");

        return null;
    }

    /// <summary>
    /// Every generated declaration file, as a pattern the editor configuration can include.
    /// </summary>
    private static string GeneratedTypesInclude(CompilationLayout layout, string relativeTo)
    {
        var relative = Normalise(Path.GetRelativePath(relativeTo, layout.GeneratedTypesDirectory));

        if (!relative.StartsWith('.') && !Path.IsPathRooted(relative))
        {
            relative = "./" + relative;
        }

        // Recursive: the namespace facades sit a directory below.
        return $"{relative}/**/*.d.ts";
    }

    /// <summary>
    /// Writes the ambient declarations for static asset imports and returns the path to include.
    /// </summary>
    /// <remarks>
    /// Written here rather than by a build step, for the same reason the Preact declarations and the
    /// pending stand-in are: whatever points the compiler at a file is what has to make sure the
    /// file is there. Both configurations are produced by this class, so both get it.
    /// </remarks>
    private static string WriteAssetDeclarations(CompilationLayout layout, string relativeTo)
    {
        Directory.CreateDirectory(layout.WorkingDirectory);

        var path = Path.Combine(layout.WorkingDirectory, ViewAssets.DeclarationFileName);
        AssetStage.WriteFileIfChanged(path, ViewAssets.DeclarationSource());

        var relative = Normalise(Path.GetRelativePath(relativeTo, path));
        return !relative.StartsWith('.') && !Path.IsPathRooted(relative) ? "./" + relative : relative;
    }

    /// <summary>
    /// Maps each sub-path of the rendering scheme onto its declarations.
    /// </summary>
    /// <remarks>
    /// From the same list the import map is built from, so a specifier cannot type check in an
    /// editor and then fail to resolve in a browser.
    /// </remarks>
    private static void AddSubModulePaths(JsonObject paths, string runtimeDirectory)
    {
        foreach (var name in RuntimeAssets.PublicSubModules)
        {
            paths[$"{RuntimeAssets.ModuleSpecifier}/{name}"] =
                new JsonArray($"{runtimeDirectory}/{name}.d.ts");
        }
    }

    /// <summary>
    /// The alias JsxCore provides for the views directory, matching what every Next.js and Vite
    /// template scaffolds.
    /// </summary>
    /// <remarks>
    /// Generated rather than left to the application, so it works in a fresh project, and because
    /// only an alias JsxCore knows about can be rewritten into something a browser resolves.
    /// </remarks>
    public const string ViewsAlias = "@/*";

    /// <summary>
    /// Merges what the application configured over what was generated.
    /// </summary>
    /// <remarks>
    /// <c>paths</c> is merged key by key rather than replaced. Assigning it wholesale is the
    /// obvious reading of "extra options merged into the generated tsconfig", and it used to drop
    /// every mapping JsxCore had just made: the <c>dotnet:</c> schemes and the framework's own
    /// declarations all stopped resolving because an application added one alias of its own.
    /// </remarks>
    private static void ApplyUserOptions(JsonObject compilerOptions, JsxCoreOptions options)
    {
        foreach (var (key, value) in options.CompilerOptions)
        {
            var node = JsonSerializer.SerializeToNode(value);

            if (key == "paths" && node is JsonObject userPaths
                && compilerOptions["paths"] is JsonObject generated)
            {
                foreach (var (specifier, target) in userPaths.ToList())
                {
                    generated[specifier] = target?.DeepClone();
                }

                continue;
            }

            compilerOptions[key] = node;
        }
    }

    private static JsonObject MergePaths(JsonObject compilerOptions, JsonObject basePaths)
    {
        if (compilerOptions["preactPaths"] is JsonObject extra)
        {
            compilerOptions.Remove("preactPaths");
            foreach (var (specifier, value) in extra.ToList())
            {
                basePaths[specifier] = value?.DeepClone();
            }
        }

        return basePaths;
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    // tsconfig paths use forward slashes on every platform.
    private static string Normalise(string path) => path.Replace('\\', '/');
}
