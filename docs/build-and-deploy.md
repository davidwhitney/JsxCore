# Build and deploy

← [Documentation index](README.md)

---

## Build modes

JsxCore can compile views at three different points. They are not exclusive. Most applications use
the first in development and the second and third in production.

### 1. On demand, at startup *(default)*

`AddJsxCore()` compiles every view during application startup, before the first request is served.
In Development it also watches the views directory and recompiles on change.

```csharp
options.CompileOnStartup = true;    // default
options.WatchForChanges = true;     // default: true in Development only
```

Requires the TypeScript toolchain on the machine running the app. Compilation takes tens of
milliseconds for a typical view tree, which is fine for development and for many production
deployments.

### 2. At build time, via MSBuild

The package ships an MSBuild target that compiles views during `dotnet build` and `dotnet publish`,
and carries the compiled JavaScript into the publish output.

```xml
<PropertyGroup>
  <JsxCoreCompileOnBuild>true</JsxCoreCompileOnBuild>   <!-- default -->
  <JsxCoreTypeChecking>error</JsxCoreTypeChecking>      <!-- fail the build on type errors -->
  <JsxCoreViewsDirectory>Views</JsxCoreViewsDirectory>
</PropertyGroup>
```

The target locates the same native compiler, walking up from the project directory to find
`node_modules`. If it cannot find one it emits warning `JSX0001` and leaves compilation to startup,
rather than failing your build.

Finding it, reading `package.json` and writing the compiler configuration are done by a small tool
the package ships under `tools/`, which the target invokes. That is not an implementation detail
you should have to care about, with one exception worth knowing: **the build-time and run-time
compiler settings are produced by the same code**, so they cannot drift apart. `JsxCoreToolPath`
points at it if you ever need to relocate it, and `JSX0006` is what you see when it is missing.

### The build installs its own dependencies

A clean checkout needs no npm step of its own. When the compiler is missing, the build fetches it
before compiling:

```
JsxCore: restoring with native: typescript
JsxCore: fetching @typescript/typescript-linux-x64@7.0.2
```

So a pipeline is just:

```yaml
- run: dotnet publish -c Release
```

Nothing runs on a build where the packages are already present, so this costs nothing after the
first time.

**When a lock file exists it is installed from directly**, exactly as pinned, without rewriting
`package-lock.json`. That keeps builds reproducible and the working tree clean. Resolving afresh
against the registry is only a fallback, when there is no lock file or when the lock file did not
produce a usable compiler.

Committing `package.json` and `package-lock.json` is still the right thing to do: it pins versions
and lets the build take the reproducible path. If you would rather manage packages entirely
yourself:

```xml
<JsxCoreAutoInstallDependencies>false</JsxCoreAutoInstallDependencies>
```

With that set, a build with no compiler emits warning `JSX0001` and compiles nothing, leaving the
work to application startup.

### 3. Precompiled only, at run time *(automatic for Release)*

Because publish output already contains compiled views, production needs no toolchain at all — and
you do not have to ask for it. A **Release** build compiles the views, carries them into the output
and records the fact on your assembly, so the application serves what the build produced.

Startup then verifies that compiled output *exists* instead of verifying the toolchain, and
compilation, watching and hot reload are all disabled. If the output is missing, startup fails with
an explanation rather than serving an application that 500s on every view.

Set it yourself only to override that:

```csharp
builder.AddJsxCore(options =>
{
    options.PrecompiledOnly = false;   // compile at startup even in Release
});
```

Debug builds leave it off, because compiling at startup is what makes a view change appear without
a rebuild.

Views that import [npm packages](npm-packages.md) are the exception: those are read from
`node_modules` at run time, so they still have to be present. Everything in your `dependencies` is
copied into the publish output automatically; restoring on the server from the committed lock file
works too, with npm or without it. `devDependencies`, the compiler included, are left out of both.

---

## Type-checking modes

Independent of *when* compilation happens, `options.TypeChecking` controls how strict it is:

| Mode | Behaviour |
|---|---|
| `Off` | Transpile only (`--noCheck`). Fastest; no type errors reported at all |
| `Warn` *(default)* | Type errors are logged and shown in the dev error overlay; the app keeps serving |
| `Error` | Type errors throw; compilation fails and requests for views fail |

`Warn` is the right default in development, because a type error in one view should not take down
the others.

Use `Error` where a mistake should stop the line:

```csharp
options.TypeChecking = builder.Environment.IsDevelopment()
    ? TypeCheckingMode.Warn
    : TypeCheckingMode.Error;
```

```xml
<JsxCoreTypeChecking>error</JsxCoreTypeChecking>
```

With `error`, the MSBuild target raises `JSX0002` and fails the build the same way a C# error
would. With `warn` it raises `JSX0003` instead.

---

## Deployment

The recommended production setup is the default one:

```csharp
builder.AddJsxCore();
```

```bash
dotnet publish -c Release
```

Views are compiled during publish, the JavaScript is carried into the output, and the running
application needs **no `node_modules`, no TypeScript and no Node.js**. Nothing in `Program.cs` says
so, because the build already did.

If you would rather compile at startup in production, set `options.PrecompiledOnly = false` and make
sure the TypeScript package is present on the server.

### What the framework needs

**Preact needs nothing.** It ships inside the JsxCore package, and the view engine stages it out of
the assembly at startup, so there is nothing to publish and nothing to install. If you have
installed your own Preact, the build publishes its `.mjs` files and manifests under `node_modules/`
— a few tens of kilobytes rather than the whole package — and the running application resolves them
the same way it does in development.

**React is restored from npm**, like any other dependency, and `react` and `react-dom` are carried
into the publish output because they are regular `dependencies`. Their `@types` packages are dev
dependencies and are left out.

You need npm on neither the server nor the **build** machine: JsxCore restores from the registry
itself.

### Containers

The runtime image needs nothing beyond the .NET runtime, and the build
image needs nothing beyond the SDK, because packages are restored without Node:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

No npm and no Node appear in either stage. The SDK image is enough, because JsxCore fetches the
compiler binary from the registry itself.

## Minification and compression

Both are on for Release builds and off for Debug, because they cost build time and obscure the
source, which is the wrong trade while developing. Either can be turned off:

```xml
<PropertyGroup>
  <JsxCoreMinify>false</JsxCoreMinify>
  <JsxCoreCompressAssets>false</JsxCoreCompressAssets>
</PropertyGroup>
```

The settings are stamped onto your assembly, so an application that compiles views at startup obeys
the same answer as one serving what the build produced. `options.Minify` and
`options.CompressAssets` override it from application code if you need to decide at run time.

**Minification is esbuild**, restored as a dev dependency alongside the TypeScript compiler when it
is switched on, and run the same way: a native binary, no Node, nothing on the server. It never
fails a build. If the binary is missing, or a package is written in something it refuses, the
original is served and a warning says so — a larger payload is a worse outcome than a smaller one,
and a better outcome than an application that will not start.

It covers everything the browser downloads: your compiled views, the framework, and the npm
packages, which are usually the bulk of it. Views are minified both by the build, for a
a precompiled deployment where nothing recompiles them later, and at startup for an application
that compiles then.

**Compression** is Brotli where the client takes it and gzip otherwise, computed once per build and
held in memory rather than repeated per request. Turn it off if a reverse proxy or CDN in front of
the application already compresses, since doing it twice costs CPU and saves nothing.

On the React sample, the whole client payload:

| | bytes | |
|---|---|---|
| as published by npm | 1,762,890 | |
| minified | 606,756 | 66% smaller |
| minified, gzipped | 190,964 | |
| minified, Brotli | 161,547 | **91% smaller than the first row** |

Minified and unminified assets never share a URL: the setting is part of the build id, so turning
minification on moves every URL rather than serving different bytes from one a browser has already
cached for a year.

### Caching and CDNs

Compiled modules are served from `/_jsx/v{buildId}/...` with a one-year immutable `Cache-Control`.
The build id is a content hash, so it is safe to put a CDN in front of that path with a long TTL:
a deployment that changes a view changes the URL.

A deployment that changes *nothing* produces the same build id, so caches survive it. Upgrading
JsxCore itself always changes it: the version that transformed the assets is part of the hash, so a
new release cannot serve different content at a URL a browser already holds for a year.

### Generated model types in CI

[Model type declarations](model-types.md) are generated during the build, from the assembly the
build produced, so a fresh clone type-checks without the application ever being started.

The build cannot see options set in `Program.cs`, so it generates with the defaults. If you have
customised `AutoExport`, the JSON naming policy or `EnumsAsStrings`, and CI runs
`JsxCoreTypeChecking=error`, commit the declarations instead so CI checks against the exact set:

```csharp
options.TypeDefinitions.OutputPath = "Views/generated";
```

---

## What ends up in the publish output

```
publish/
├── MyApp.dll
├── obj/JsxCore/js/          ← compiled views
├── node_modules/react/...      ← only in React mode, or if you installed your own Preact
├── wwwroot/
└── ...
```

The JsxCore runtime itself is not a file at all. It is embedded in `JsxCore.dll` and served from
the assembly manifest. Nothing needs installing on the server.
