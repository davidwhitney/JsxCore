# Troubleshooting

← [Documentation index](README.md)

---

## Startup failures

### `JsxCoreEnvironmentException`: could not find the TypeScript compiler

The message lists every path searched and the exact install command. Usual causes:

- The automatic install did not run or did not succeed. The message says which, and always ends
  with the command to run by hand: `npm install --save-dev typescript@^7`
- No network access to the npm registry from the machine, so nothing could be fetched. Install the
  package yourself and set `options.AutoInstallDependencies = DependencyInstallMode.Never`
- `options.PackageManager` is `"npm"` and npm is not on PATH. Set `options.NpmPath`, or clear
  `PackageManager` and let JsxCore restore, which needs nothing installed
- The content root differs from where `node_modules` lives, which is common under test hosts.
  Add `options.AdditionalToolchainSearchPaths.Add(repositoryRoot)`, or set
  `options.TypeScriptCompilerPath` explicitly. See [Testing](testing.md).
- You are on a platform TypeScript does not publish a native binary for. Set
  `TypeScriptCompilerPath` to any compiler you do have.

### `JsxCoreEnvironmentException`: requires TypeScript 7 or later

JsxCore depends on the native compiler and on `rewriteRelativeImportExtensions`, which earlier
versions do not provide. Raise the range in `package.json` and rebuild, or run
`dotnet npm add typescript --version ^7 --dev`.

### `JsxCoreEnvironmentException`: no compiled views were found

The application is serving precompiled views — which a Release build turns on by itself — but the
output has none. Check that the package's
build targets ran, that `JsxCoreCompileOnBuild` is not `false`, and that
`options.WorkingDirectory` matches `JsxCoreWorkingDirectory`.

### `error JSX0005: JsxCore needs npm packages that are not installed`

`package.json` declares packages that are not in `node_modules`, and
`JsxCoreAutoInstallDependencies` is `false` so the build will not fetch them. Restore them where
your `package.json` lives, or turn automatic installation back on.

The check exists because the alternative is worse. A missing package does not fail compilation, so
without it the build succeeds and the view fails to render later with an error about a module,
which reads as a JsxCore fault rather than a missing install.

### `error JSX0007: JsxCore does not know the framework`

`<JsxCoreFramework>` takes `preact` or `react`. `preact` is the default and can be omitted. See
[Runtimes](runtimes.md#switching-frameworks).

### `warning JSX0006: JsxCore could not find its build tool`

The tool the targets invoke, normally at `tools/net8.0/JsxCore.Tool.dll` inside the package, is not
where it was expected. Views are not compiled at build time as a result. Usually a damaged package
cache: `dotnet nuget locals all --clear` and restore again. Set `JsxCoreToolPath` if you have
deliberately relocated it.

### `warning JSX0004: JsxCore could not install`

A restore ran and some packages are still absent. Usual causes:

- A name or version in `package.json` is wrong, so the whole install failed. The output above the
  warning names the package it could not fetch.
- No network access, or a private registry that needs credentials. Authenticating to a private
  registry needs npm: set `<JsxCoreUseNpm>true</JsxCoreUseNpm>` so your `.npmrc` is used.
- The package runs install scripts, which are Node programs and are not run natively. Same fix.
- `<JsxCoreUseNpm>true</JsxCoreUseNpm>` is set and npm is not on PATH. Install Node.js, set
  `<JsxCoreNpm>` to its path, or drop the flag and let JsxCore restore.

Views importing the listed packages will fail to render.

### "configured to minify assets but esbuild was not found"

Minification is on and the esbuild binary is not in `node_modules`. Assets are served unminified,
which is why this is a warning. Build the project to restore it, or add it yourself with
`dotnet npm add esbuild --dev`. It only appears where minification is enabled, which by default
means Release builds. See [Build and deploy](build-and-deploy.md#minification-and-compression).

A **published** application never reports this. esbuild is a development dependency and is
deliberately not published; the build and the publish step minified everything before it got there,
so there is nothing left to do and nothing to warn about.

If the machine is one esbuild publishes no binary for, set `JsxCoreMinify` to `false` rather than
carrying the warning.

### `warning JSX0001` during build, and no compiled views

The build could not find the TypeScript compiler and could not install it either. Usual causes:

- No network access from the build machine, so the compiler could not be fetched.
- Installation is switched off with `<JsxCoreAutoInstallDependencies>false</JsxCoreAutoInstallDependencies>`.
  Install `typescript@^7` as a dev dependency yourself.
- The lock file and `package.json` are out of step, so restoring from it did not produce a compiler.
  Delete `package-lock.json` and build again to have it rewritten, then commit the result.

The build carries on regardless, leaving compilation to application startup, so this is a warning
rather than an error. It does become fatal later, because a Release build then starts an
application that expects compiled views and finds none. Building Debug, or setting
`options.PrecompiledOnly = false`, compiles at startup instead.

---

## Package management problems

### `dotnet npm` says the command could not be found

The tool is not installed, or is installed for a different project. Install it globally with
`dotnet tool install -g JsxCore.Npm`, or, if the repository has a tool manifest, run
`dotnet tool restore` from the repository root. See
[Package management](package-management.md#installing-the-tool).

Note that this is entirely separate from the build: `dotnet build` restores packages whether or not
the tool is installed.

### `there is no package-lock.json`

`dotnet npm ci` installs exactly what a lock file pins and refuses to resolve without one, the same
way npm's does. Run `dotnet npm restore` once to write the lock file, and commit it.

### `There is no package named 'x' on https://registry.npmjs.org`

The name is wrong, or the package is on a registry other than the default. `--registry` selects a
different one. A private registry needing authentication needs npm: see
[using npm instead](package-management.md#using-npm-instead).

### A package installs but does not work, and it has a build step

The native client does not run lifecycle scripts, because `preinstall`, `install` and `postinstall`
are Node programs. A package that compiles something during installation needs npm; set
`<JsxCoreUseNpm>true</JsxCoreUseNpm>`.

### `warning JSX0001` and I use pnpm

pnpm keeps transitive packages in `node_modules/.pnpm` rather than at the top level, so the
TypeScript compiler is installed but is not where JsxCore searches. Add the store as a search path:

```xml
<JsxCoreAdditionalSearchPaths>$(MSBuildProjectDirectory)/node_modules/.pnpm</JsxCoreAdditionalSearchPaths>
```

npm and Yarn need no configuration. See
[package management](package-management.md#using-npm-or-another-package-manager).

### A removed package is still in node_modules

`dotnet npm remove` deletes the named package and rewrites the lock file, but anything that package
alone brought in stays on disk. Delete `node_modules` and run `dotnet npm ci` for a clean tree. The
lock file is correct either way, so a fresh restore anywhere else is already right.

---

## Rendering problems

### A view renders blank

Check the logs for TypeScript diagnostics. In `Warn` mode compilation continues despite type
errors, so a broken view still emits and may fail at runtime. In development the error overlay
shows the diagnostics directly in the page.

If there are no diagnostics, confirm the view has a **default export that is a function**.

### `JsxViewNotFoundException`

The message lists every location probed. View names resolve through `ViewLocationFormats`;
`"Home/Index"` and `"~/Views/Home/Index.tsx"` are both valid. Check the file extension is in
`options.Extensions`.

### `JsxRenderException` with a JavaScript stack trace

The component threw. The message carries the JavaScript stack, mapped to the compiled module. Most
common causes: reading a property of a null model value, or calling a `.NET` global from a
client-rendered view.

### "server rendering is synchronous, but a component returned a Promise"

An `async` component. Server rendering is synchronous by design, because .NET calls return immediately and
nothing needs awaiting. Do async work in your endpoint and pass the result in the model.

### ".NET globals are only available during server rendering"

A client-rendered view touched `dotnet`. Either switch the view to `RenderMode.Server`, or guard the
call with `isServerRender()`.

### "cannot resolve the module 'x' during server rendering"

The package is not installed, or `options.AllowNodeModules` is off. Install it with
`dotnet npm add <name>` and restart; the error lists the `node_modules` directories that were
searched. See [Using npm packages](npm-packages.md).

### "could not resolve the asset import 'dotnet:wwwroot/…'"

No such file under the web root. The path after `dotnet:wwwroot/` is relative to that directory:
`wwwroot/images/logo.svg` is `dotnet:wwwroot/images/logo.svg`, served as `/images/logo.svg`.

The import is left as written rather than rewritten to a URL that 404s, so it fails to load in the
browser too. If the web root is not `wwwroot`, set `JsxCoreWebRootDirectory` so the build resolves
against the directory the application serves. See
[Import syntax](import-syntax.md#static-assets).

---

## Type problems

### `Cannot find module 'dotnet:MyApp'`, or a namespace in it is missing

Declarations are normally generated during the build, from the assembly it produced, so this means
that did not happen. Either the assembly could not be loaded for inspection, in which case the
build prints why, or `<JsxCoreGenerateModelTypes>false</JsxCoreGenerateModelTypes>` is set.
Imports fall back to `any` until the application runs and generates them.

If the types are there but the wrong shape, the build generated them with default options because
it cannot see what `Program.cs` configures. Running the application replaces them, or set
`TypeDefinitions.OutputPath` to a location you commit. See
[Model types](model-types.md#when-generation-happens).

### A model type is missing from the generated declarations

It is not being exported. The default convention covers `Models` and `ViewModels` namespaces plus
`[JsxModel]` types. Anything else needs `options.AutoExport` or an explicit
`TypeDefinitions.Add<T>()`. See [Model types](model-types.md#choosing-what-gets-exported).

### The editor flags `dotnet:rendering` as unresolved

`Views/tsconfig.json` is missing or stale. Delete it and rebuild; it is regenerated with the right
mappings. See [Development](development.md#editor-support).

### Types resolve in the editor but not during compilation, or vice versa

Both configs derive from the same base, so this normally means one is stale. Delete
`obj/JsxCore/tsconfig.json` and `Views/tsconfig.json` and rebuild.

---

## Browser problems

### Imports fail with "Failed to resolve module specifier"

A bare specifier JsxCore could not resolve. If it is an npm package, install it; JsxCore adds
import map entries for packages the views import. Otherwise add an entry with `options.ImportMap`.
Relative imports must include the real extension (`./Card.tsx`) so TypeScript can rewrite it to
`./Card.js`.

### Hot reload does not connect

`UseJsxCore()` must be in the pipeline, and must run **before** `UseRouting()` so asset requests
short-circuit early. Check that the environment is Development, or set `options.HotReload = true`
explicitly.

### Assets arrive compressed when something in front already compresses them

Turn JsxCore's own off with `<JsxCoreCompressAssets>false</JsxCoreCompressAssets>`. Compressing
twice costs CPU and saves nothing. JsxCore sets `Vary: Accept-Encoding` on every asset response
whenever compression is on, including uncompressed ones, so a shared cache cannot hand a compressed
body to a client that asked for neither.

### Stale JavaScript after a deploy

Should not happen, because asset URLs contain a content hash, so a change produces a new URL. The
build of JsxCore is part of that hash too, so upgrading the package moves every URL even when your
own sources are untouched. If you do see it, check that a proxy is not rewriting `/_jsx/...` paths
or stripping the version segment.

### The wrong framework appears to be running

In Development every response carries `X-JsxCore-Framework`, naming what is actually being served.
The framework is chosen by the build, so if that disagrees with `<JsxCoreFramework>` in the project
file, the running output was built from something else: rebuild, and check you are not running
publish output from a different configuration. See [Runtimes](runtimes.md#switching-frameworks).

---

## Limitations

Things to know before you commit to this:


- **Server components must be synchronous.** Because .NET calls return immediately there is no need
  for async, and an async component is rejected with a clear error.

- **Node built-ins are not available.** npm packages resolve from `node_modules` on both sides, but
  the server runs them in an embedded engine, so a package that requires `fs`, `path` or `crypto`
  fails. Browser-oriented and pure-JavaScript packages are fine.

- **Jint is an interpreter.** Server rendering is fast enough for typical views but is not V8. Keep
  heavy computation in .NET, where it belongs.

- **The render container is owned by JsxCore.** Nodes placed inside it by other scripts may be moved
  or removed.

- **Generated model types are only as good as the build can make them.** They are generated during
  the build, so a fresh clone type-checks after `dotnet build`, but the build cannot see what
  `Program.cs` configures. Customise `AutoExport`, the naming policy or `EnumsAsStrings` and the
  build's answer is an approximation until the application runs. See
  [Model types](model-types.md#when-generation-happens).

---

## Getting more detail

Turn up logging for the compilation service to see every diagnostic and every build:

```json
{
  "Logging": {
    "LogLevel": {
      "JsxCore": "Debug"
    }
  }
}
```

`JsxCompilationService.Current` exposes the last `BuildState`, including the full parsed diagnostic
list with file, line, column and code. Useful from a health check or diagnostics endpoint.
