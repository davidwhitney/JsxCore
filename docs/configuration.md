# Configuration

← [Documentation index](README.md)

Everything is configured through the callback passed to `AddJsxCore`:

```csharp
builder.AddJsxCore(options =>
{
    options.DefaultRenderMode = RenderMode.Server;
    options.Globals.Register<InventoryService>("Inventory");
});
```

---

## JsxCoreOptions

### Locations

| Option | Default | Purpose |
|---|---|---|
| `ViewsDirectory` | `"Views"` | Where views live, relative to the content root |
| `WorkingDirectory` | `"obj/JsxCore"` | Compiler output; gitignored and hidden from project trees |
| `RequestPath` | `"/_jsx"` | Base path compiled modules are served from |
| `WebRootDirectory` | the host's web root, else `"wwwroot"` | What `dotnet:wwwroot/…` imports resolve against |
| `Extensions` | `.tsx`, `.jsx` | File extensions treated as views, in probe order |
| `ViewLocationFormats` | `{Views}/{controller}/{view}`, `{Views}/Shared/{view}`, `{Views}/{view}` | How a view name maps to a path |
| `AreaViewLocationFormats` | area-aware equivalents | Tried first when the request has an area |

### Rendering

| Option | Default | Purpose |
|---|---|---|
| `DefaultRenderMode` | `Client` | Mode used when a result does not specify one |
| `EnableReactCompatibility` | `true` | In Preact mode, map `react`/`react-dom` to `preact/compat` |
| `ViewEngineOrder` | `0` | Position among MVC view engines; `0` beats Razor |
| `JsonSerializerOptions` | web defaults | How the model is serialised, and the basis for generated types |

### Compilation

| Option | Default | Purpose |
|---|---|---|
| `TypeChecking` | `Warn` | `Off`, `Warn` or `Error` |
| `CompileOnStartup` | `true` | Compile everything during startup |
| `PrecompiledOnly` | follows the build | Serve prebuilt output with no toolchain. On for Release, off for Debug; set it to override |
| `WatchForChanges` | dev only | Recompile when sources change |
| `HotReload` | dev only | Serve the hot reload client and endpoint |
| `AutoInstallDependencies` | `Development` | `Never`, `Development` or `Always`: when *startup* may restore packages. The build restores in any configuration |
| `Minify` | follows the build | Minify served JavaScript. Null takes the project file's answer |
| `MinifierPath` | auto | Explicit path to the esbuild binary |
| `CompressAssets` | follows the build | Compress asset responses. Null takes the project file's answer |
| `NpmPath` | auto | Explicit path to npm |
| `DependencyInstallTimeout` | 5 minutes | Limit for a single npm command |
| `OnBootstrapMessage` | console | Where install progress is reported |
| `TypeScriptCompilerPath` | auto | Explicit compiler path, skipping discovery |
| `AdditionalToolchainSearchPaths` | empty | Extra roots to search for `node_modules` |
| `MinimumTypeScriptMajorVersion` | `7` | Lowest acceptable compiler version |
| `CompilerOptions` | empty | Extra options merged into the generated tsconfig |
| `GenerateEditorTsConfig` | `true` | Write a tsconfig beside the views for editors |

`WatchForChanges` and `HotReload` are `bool?`. `null` means "on in Development, off elsewhere".

### Content

| Option | Default | Purpose |
|---|---|---|
| `ImportMap` | empty | Extra bare specifier mappings for the browser, which win over generated ones |
| `AllowNodeModules` | `true` | Let views import [npm packages](npm-packages.md), server and browser |
| `PackageManager` | unset (native) | Name a client: `native` needs nothing installed, `npm` runs the npm on the machine |
| `Globals` | empty | .NET objects exposed to server-rendered views |
| `ContextValues` | empty | Values added to every view's `context` prop |
| `AutoExport` | convention | Which .NET types get TypeScript declarations |

### Nested

| Option | |
|---|---|
| `Document` | The generated HTML shell |
| `ServerRendering` | JavaScript engine limits |
| `TypeDefinitions` | Generated type settings |

---

## Document

Controls the HTML wrapper. Every one of these can also be
[set per response](returning-views.md#per-response-document-settings).

| Option | Default | Purpose |
|---|---|---|
| `ContainerId` | `"jsxcore-root"` | Element the view renders into |
| `ModelElementId` | `"jsxcore-model"` | Script tag holding the serialised model |
| `Language` | `"en"` | `lang` attribute on `<html>` |
| `DefaultTitle` | `""` | Title when a view exports none |
| `HeadContent` | `""` | Raw markup appended to `<head>` |
| `Nonce` | none | Per-request CSP nonce for the scripts JsxCore writes |
| `BodyContent` | `""` | Raw markup appended to `<body>` |
| `BodyAttributes` | empty | Attributes on `<body>` |
| `Template` | none | Replaces the document writer entirely |

---

## ServerRendering

| Option | Default | Purpose |
|---|---|---|
| `Timeout` | 5 seconds | Wall clock limit for a single render |
| `MaxRecursionDepth` | `256` | Guards runaway recursion |
| `MaxPooledEngines` | processor count | Concurrent server renders before queueing |
| `ExposeCamelCaseMembers` | `true` | Expose .NET members under camelCase aliases too |

---

## TypeDefinitions

| Option | Default | Purpose |
|---|---|---|
| `Enabled` | `true` | Generate TypeScript from .NET types at all |
| `AutoExport` | `null` | A `TypeSource`; `null` means the built-in convention |
| `ConventionalNamespaceNames` | `Models`, `ViewModels` | Namespace segments treated as holding view models |
| `ApplicationAssembly` | from the environment | Assembly the convention scans |
| `OutputPath` | `obj/JsxCore/types` | Where declarations are written |
| `MirrorNamespaces` | `true` | One TypeScript namespace per .NET namespace |
| `TrimNamespacePrefix` | `null` | Strip a root namespace from generated names |
| `EnumsAsStrings` | `null` | `null` follows the enum converter |
| `IncludeFields` | `false` | Include public fields as well as properties |
| `Add<T>()` | n/a | Register a type explicitly, alongside `AutoExport` |

The specifier is not configurable: the module is named after the assembly it describes. See
[Model types](model-types.md) for the `TypesFrom` factories.

### Module specifiers

| Specifier | Holds |
|---|---|
| `dotnet:<AssemblyName>` | Types generated from that assembly, reached through their .NET namespace |
| `dotnet:<AssemblyName>/<Namespace>` | The same types, imported by name from the namespace they live in |
| `dotnet:globals` | The .NET objects registered with `options.Globals`, one named export each |
| `dotnet:rendering` | `isServerRender`, and the types a view is handed: `ViewProps`, `HeadDescriptor` |
| `dotnet:wwwroot/<path>` | A static file, as the URL it is served from |

One rule: `dotnet:` is the .NET side of the application, meaning an assembly by name or one of the
three names JsxCore reserves: `globals`, `rendering` and `wwwroot/`. It is not an npm package and
does not resolve like one; the browser is told about it through the import map, and TypeScript through
`paths`. Reserved names are matched first and win over an assembly of the same name, which a
PascalCase assembly cannot collide with anyway. See [Import syntax](import-syntax.md).

---

## Globals

| Method | Lifetime |
|---|---|
| `Register<TService>(name?)` | Resolved per render from the request's service scope |
| `Register(name, instance)` | One shared instance, which must be thread-safe |
| `Register(name, factory)` | Whatever your factory decides |
| `Remove(name)` | Unregister |

See [.NET interop](dotnet-interop.md).

---

## MSBuild properties

Set these in your `.csproj`. They control [build-time compilation](build-and-deploy.md).

| Property | Default | Purpose |
|---|---|---|
| `JsxCoreCompileOnBuild` | `true` | Compile views during `dotnet build` and `dotnet publish` |
| `JsxCoreTypeChecking` | `warn` | `error`, `warn` or `off` |
| `JsxCoreFramework` | `preact` | `preact` ships inside JsxCore; `react` is restored from npm by the build |
| `JsxCoreViewsDirectory` | `Views` | Where views live |
| `JsxCoreManifestDirectory` | beside the project | Directory holding the `package.json` to use, for a solution sharing one |
| `JsxCoreWorkingDirectory` | `$(BaseIntermediateOutputPath)JsxCore\` | Compiler output |
| `JsxCoreWebRootDirectory` | `wwwroot` | What `dotnet:wwwroot/…` imports resolve against |
| `JsxCoreAutoInstallDependencies` | `true` | Install missing npm packages during the build |
| `JsxCoreMinify` | Release only | Minify what is served to the browser, with esbuild |
| `JsxCoreCompressAssets` | Release only | Compress assets on the way out: Brotli, or gzip |
| `JsxCorePrecompiled` | Release only | Serve the views the build compiled instead of compiling at startup |
| `JsxCoreMinifierPath` | auto | Explicit path to the esbuild binary |
| `JsxCoreNpm` | probed | Path to npm, if it is not on PATH |
| `JsxCorePackageManager` | `native` | `native` talks to the registry directly; `npm` shells out to npm |
| `JsxCoreUseNpm` | `false` | Shorthand for `JsxCorePackageManager=npm` |
| `JsxCoreGenerateModelTypes` | `true` | Generate TypeScript declarations from .NET models during the build |
| `JsxCoreEmitViewLocationAnnotations` | `true` | Emit the annotations that let an IDE resolve `View()` calls |
| `JsxCoreViewExtensions` | `.tsx;.jsx` | Extensions those annotations describe |
| `JsxCoreAdditionalSearchPaths` | empty | Extra roots to search for `node_modules`, as pnpm needs |
| `JsxCoreCompilerPath` | auto | Explicit path to the TypeScript binary |
| `JsxCoreGenerateEditorTsConfig` | `true` | Write the editor tsconfig at build time |
| `JsxCoreToolPath` | in the package | The build tool the targets invoke, under `tools/net8.0/` |
| `JsxCoreToolCommand` | `dotnet exec ...` | How that tool is run, if you need to change the host |

### npm packages

Packages come from `package.json`, not the project file. Everything in `dependencies` is served to
the browser and copied into the publish output; `devDependencies` are build-time only. See
[npm packages](npm-packages.md) for importing them and
[package management](package-management.md) for installing them.

| Property | Default | Purpose |
|---|---|---|
| `JsxCoreShowNpmPackagesInIde` | `true` | Show installed packages in the project tree |
| `JsxCoreIdePackageFolder` | `package.json` | Virtual folder they appear under, shadowing the manifest |

### The dotnet npm tool

Not configured through the project file. Installed separately and run by hand:

```bash
dotnet tool install -g JsxCore.Npm
dotnet npm add marked --version ^12
```

| Command | Purpose |
|---|---|
| `add`, `install`, `i` | Resolve and install a package, recording it in `package.json` |
| `remove`, `rm`, `un`, `uninstall` | Drop it, delete it, and re-resolve the rest |
| `list`, `ls` | What is declared, and whether it is installed |
| `restore` | Install what the lock file pins, resolving `package.json` if there is none |
| `ci` | Install what the lock file pins, failing if there is no lock file |
| `init` | Create a `package.json` |

| Option | Purpose |
|---|---|
| `--version <RANGE>`, `-v` | Version range to add. Defaults to the latest release |
| `--dev`, `-D`, `--save-dev` | Add to `devDependencies` |
| `--project <PATH>`, `--prefix` | Directory or project file to act on. Defaults to the current directory |
| `--registry <URL>` | Registry to resolve from |

Full reference in [package management](package-management.md#on-the-command-line).

### Diagnostics

| Code | Severity | Meaning |
|---|---|---|
| `JSX0001` | Warning | The TypeScript compiler was not found; compilation deferred to startup |
| `JSX0002` | Error | TypeScript reported errors and `JsxCoreTypeChecking` is `error` |
| `JSX0003` | Warning | TypeScript reported errors and `JsxCoreTypeChecking` is not `error` |
| `JSX0004` | Warning | Packages in `package.json` could not be installed |
| `JSX0005` | Error | Packages are missing and `JsxCoreAutoInstallDependencies` is `false` |
| `JSX0006` | Warning | The build tool could not be found, so views were not compiled |
| `JSX0007` | Error | `JsxCoreFramework` names a framework JsxCore does not know |

---

## Exceptions

| Type | Thrown when |
|---|---|
| `JsxCoreEnvironmentException` | Something needed is missing at registration: the toolchain, the views directory, a writable working directory, or compiled output when the application is serving precompiled views |
| `JsxCompilationException` | Compilation failed and `TypeChecking` is `Error` |
| `JsxViewNotFoundException` | A view could not be located; carries every path probed |
| `JsxRenderException` | Server-side rendering threw; carries the JavaScript stack trace |
| `JsxCoreException` | Base type for all of the above |
