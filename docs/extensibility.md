# Extensibility

← [Documentation index](README.md)

---

## Replace the whole document

`Document.Template` takes over rendering of the entire response body. It receives everything
JsxCore knows about the render:

```csharp
options.Document.Template = context => $"""
    <!DOCTYPE html>
    <html lang="{context.Document.Language}">
      <head>
        <title>{context.Head?.Title}</title>
      </head>
      <body>
        <div id="{context.Document.ContainerId}">{context.ServerHtml}</div>
      </body>
    </html>
    """;
```

`DocumentContext` exposes:

| Property | |
|---|---|
| `ViewName` | The view being rendered |
| `RenderMode` | Client, Server or ServerAndClient |
| `ServerHtml` | Markup from the server pass, or null |
| `Head` | The view's `head` export, if any |
| `ModelJson` / `ContextJson` | Already serialised |
| `ModuleUrl` | Build-id-versioned URL of the compiled view |
| `ImportMap` | Bare specifier → URL |
| `ClientSpecifier` | Where the mount helper comes from for the active runtime |
| `HotReloadEnabled`, `HotReloadClientUrl`, `HotReloadEndpoint` | |
| `Document` | The effective `DocumentOptions` for this response |
| `Options` | The whole `JsxCoreOptions` |

A template is responsible for the entire body, so if you write one you must emit the import map,
the model script and the mount script yourself for client-rendered views. For most customisation
the individual `DocumentOptions` settings are the better tool.

Set `result.DocumentTemplate` to do this for a single response instead.

---

## Shape the standard document

```csharp
options.Document.ContainerId = "app";
options.Document.ModelElementId = "app-model";
options.Document.Language = "en-GB";
options.Document.DefaultTitle = "My application";
options.Document.HeadContent = "<link rel=\"stylesheet\" href=\"/site.css\">";
options.Document.BodyContent = "<script src=\"/analytics.js\"></script>";
options.Document.BodyAttributes["data-theme"] = "dark";
```

All of these can be [overridden per response](returning-views.md#per-response-document-settings).

---

## Pass ambient data to every view

```csharp
options.ContextValues["environment"] = builder.Environment.EnvironmentName;
options.ContextValues["version"] = typeof(Program).Assembly.GetName().Version?.ToString();
```

Per request:

```csharp
app.Use(async (context, next) =>
{
    context.AddJsxContext("user", context.User.Identity?.Name);
    await next();
});
```

Both arrive as the component's `context` prop, alongside the request `path`.

---

## Add module specifiers

The generated import map wires up the active runtime and the generated types. Add your own entries
for other bare specifiers:

```csharp
options.ImportMap["chart.js"] = "https://esm.sh/chart.js@4";
options.ImportMap["@my/design-system"] = "/lib/design-system/index.js";
```

Entries you add here win over the ones JsxCore generates, so this is also how you point an installed
package at a CDN build instead. Packages that are simply installed need no entry at all: see
[Using npm packages](npm-packages.md). A package that only works in a browser should still be
imported from a client-rendered view, or guarded behind `isServerRender()`.

---

## Change where views live

```csharp
options.ViewsDirectory = "Pages";
options.Extensions.Add(".mtsx");

options.ViewLocationFormats.Clear();
options.ViewLocationFormats.Add("{ViewsDirectory}/{1}/{0}");
options.ViewLocationFormats.Add("{ViewsDirectory}/Shared/{0}");
```

`{0}` is the view name, `{1}` the controller, `{2}` the area. `{ViewsDirectory}` expands to the
configured directory. `AreaViewLocationFormats` is the area-aware equivalent and is tried first
when a request has an area.

Formats are probed in order, and each is tried with every configured extension, so `.tsx` beats
`.jsx` because of the order in `Extensions`.

---

## Influence the TypeScript compilation

Anything in `CompilerOptions` is merged into the generated `tsconfig.json`, last write wins:

```csharp
options.CompilerOptions["strict"] = false;
options.CompilerOptions["target"] = "es2020";
options.CompilerOptions["experimentalDecorators"] = true;
options.CompilerOptions["paths"] = new Dictionary<string, string[]>
{
    ["@components/*"] = ["./Views/Shared/*"]
};
```

You are overriding a working configuration, so be aware of what you are changing. In particular,
`jsx`, `jsxImportSource`, `module`, `moduleResolution` and `rewriteRelativeImportExtensions` are
load-bearing. Changing them will break either the browser's module resolution or the server
renderer, or both.

---

## Coexist with Razor

JSX views are inserted at position `ViewEngineOrder` in MVC's view engine list. `0` (the default)
means JSX wins; a view that JsxCore cannot find falls through to Razor, so you can migrate a page
at a time in either direction.

```csharp
options.ViewEngineOrder = 1;   // Razor first, JSX as the fallback
```

---

## React to compilation

`JsxCompilationService` is a public singleton:

```csharp
var compilation = app.Services.GetRequiredService<JsxCompilationService>();

compilation.BuildCompleted += state =>
    logger.LogInformation("Build {Id}: {Errors} error(s) in {Ms}ms",
        state.BuildId, state.Result.Errors.Count, state.Result.Duration.TotalMilliseconds);

await compilation.CompileAsync();          // force a rebuild
var current = compilation.Current;          // last BuildState, with full diagnostics
var toolchain = compilation.Toolchain;      // which compiler is in use, or null if precompiled
```

`BuildCompleted` fires after watch-triggered rebuilds, which is what the hot reload service listens
to. Useful for build dashboards, custom notifications, or invalidating your own caches.

`BuildState.Result.Diagnostics` gives you the parsed TypeScript diagnostics with file, line, column,
code and message.

---

## Tune the server-side engine

```csharp
options.ServerRendering.Timeout = TimeSpan.FromSeconds(2);
options.ServerRendering.MaxRecursionDepth = 128;
options.ServerRendering.MaxPooledEngines = 8;
options.ServerRendering.ExposeCamelCaseMembers = false;   // exact .NET names only
```

Engines are pooled and reused across requests, keeping their parsed module graph. `MaxPooledEngines`
bounds how many server renders run concurrently before they queue. The default is the processor
count.

A rebuild discards pooled engines automatically.

---

## Serve assets from somewhere else

```csharp
options.RequestPath = "/assets/jsx";
```

Changes the base path for compiled modules, the runtime and the hot reload endpoint together. Useful
behind a reverse proxy that reserves particular prefixes.

---

## Change how the model is serialised

```csharp
options.JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Converters = { new JsonStringEnumConverter() }
};
```

This is used for the model in the page, the model passed to the server renderer, **and** as the
basis for [generated model types](model-types.md), so the TypeScript follows whatever you configure
here automatically.
