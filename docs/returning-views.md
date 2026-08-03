# Returning views

← [Documentation index](README.md)

Returning a view from a minimal API or an MVC controller, and overriding document settings
for one response.

---

## Minimal APIs

```csharp
app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", model));
```

One method, whatever the view does. Where it renders comes from the view's `"use client"` or
`"use server"` directive. Pass a mode to decide at the endpoint instead:

```csharp
Results.Extensions.Jsx("Home/Search", model, RenderMode.ServerAndClient);
```

See [Render modes](render-modes.md#the-order) for the order of precedence.

`JsxResults.Jsx(...)` is available as a plain static if you prefer not to extend `Results`.

---

## MVC controllers

JsxCore registers itself as an `IViewEngine`, so `return View()` finds `Views/Home/Index.tsx`
through the normal view location logic. There is no JsxCore-specific code in the controller:

```csharp
public class HomeController : Controller
{
    public IActionResult Index() => View(new IndexModel("World"));
}
```

### Your IDE resolves these too

Rider and ReSharper resolve `View()` by looking for Razor files in the conventional places, so a
view engine serving `.tsx` would otherwise leave every action reporting a view it cannot find.

JsxCore ships a source generator that emits the assembly annotations JetBrains publishes for exactly
this, describing where JsxCore actually looks. Nothing to configure, and nothing reaches the
compiled output: the attributes are conditional on a symbol nothing defines. It runs in the
compilation your editor is already running, so views resolve on opening a project rather than after
a build.

```xml
<PropertyGroup>
  <JsxCoreEmitViewLocationAnnotations>false</JsxCoreEmitViewLocationAnnotations>
</PropertyGroup>
```

An IDE cannot see run time configuration, so an application that changes
`options.ViewLocationFormats` in code gets annotations describing the defaults. Set
`JsxCoreViewLocationFormats` in the project file to match.

Be explicit when you want a particular mode:

```csharp
public IActionResult Report() => this.Jsx("Home/Report", model, RenderMode.Server);
public IActionResult Index()  => this.Jsx("Home/Index", model);
```

### View resolution

View names resolve through `ViewLocationFormats`, which default to:

```
{ViewsDirectory}/{controller}/{view}
{ViewsDirectory}/Shared/{view}
{ViewsDirectory}/{view}
```

with `.tsx` tried before `.jsx`.

**The extension decides how a name is read.** Without one it is a view name and goes through the
formats above. With one it is a file, and is opened:

```csharp
this.Jsx("Home/Index", model);                 // a view name
this.Jsx("Home/Index.tsx", model);             // a file, under the views directory
this.Jsx("~/Pages/Home.tsx", model);           // a file, from the content root
this.Jsx("/srv/app/Views/Home/Index.tsx");     // a file, by absolute path
```

Everywhere except Windows an absolute path and a views-relative name are spelled identically, so
a leading slash settles nothing on its own: `/Home/Index` is a view name and
`/srv/app/Views/Home/Index.tsx` is a path. `~/` is always a path, with or without an extension,
since saying so is what it is for.

A file named by path is opened where it is named and nowhere else, so a missing one is an error
rather than something retried as a view name.

A view that cannot be found throws `JsxViewNotFoundException`, whose message lists every path
probed.

### Coexisting with Razor

JSX views are inserted at position `ViewEngineOrder` (default `0`) in MVC's view engine list, so
they take precedence. A view JsxCore cannot find falls through to Razor, which means the two run
side by side and you can migrate a page at a time.

```csharp
options.ViewEngineOrder = 1;   // Razor first, JSX as the fallback
```

---

## Async endpoints

A component cannot be `async`, because
[server rendering is synchronous](dotnet-interop.md#why-this-is-synchronous). But **the endpoint
that builds its model is ordinary ASP.NET Core, and should be async whenever it talks to
anything**.
Database queries, HTTP calls to downstream services and any other I/O belong there, awaited with
C#'s own `async`/`await`, so the view receives a finished result.

```csharp
app.MapGet("/dashboard", async (InventoryService inventory, IOrderClient orders) =>
{
    // Ordinary async C#: run them concurrently, await, then hand over a finished model.
    var stockTask = inventory.LoadAsync();
    var recentTask = orders.GetRecentAsync(limit: 10);
    await Task.WhenAll(stockTask, recentTask);

    return Results.Extensions.Jsx(
        "Home/Dashboard", new DashboardModel(stockTask.Result, recentTask.Result));
});
```

The same in a controller:

```csharp
public sealed class DashboardController(InventoryService inventory) : Controller
{
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index()
    {
        var model = new DashboardModel(await inventory.LoadAsync());

        ViewData[JsxViewEngine.RenderModeKey] = RenderMode.ServerAndClient;
        return View(model);
    }
}
```

Awaiting in the endpoint happens once, on a thread pool thread, with the whole of ASP.NET Core's
cancellation and dependency injection available. Awaiting inside a component would mean suspending a
render, which is machinery JsxCore does not have. By the time the component runs, every value it
needs is already in the model.

A component that needs data the endpoint did not fetch has two options:
[`dotnet:globals`](dotnet-interop.md) for a synchronous in-process call during the server pass, or
[`fetch` in an effect](views-and-web-apis.md) after hydration.

---

## Per-response document settings

Every [`DocumentOptions`](configuration.md) setting can be overridden for a single response:

```csharp
app.MapGet("/report", () => Results.Extensions.Jsx("Home/Report", model, result =>
{
    result.Title = "Q3 report";                      // wins over the view's head export
    result.Language = "en-GB";
    result.HeadContent = "<link rel='stylesheet' href='/print.css'>";
    result.BodyContent = "<noscript>Enable JavaScript</noscript>";
    result.BodyAttributes["data-theme"] = "dark";
    result.ContainerId = "app";
    result.ModelElementId = "app-model";
    result.StatusCode = 200;
    result.ContentType = "text/html; charset=utf-8";
}));
```

The same properties exist on `JsxViewResult` when you construct it directly:

```csharp
public IActionResult NotFoundPage() => new JsxViewResult("Errors/NotFound")
{
    StatusCode = StatusCodes.Status404NotFound,
    Title = "Not found"
};
```

Anything left unset keeps the value configured at startup, and overrides never leak into other
responses.

### Replacing the document entirely

`result.DocumentTemplate` takes over the whole response body for one response;
`options.Document.Template` does it globally. See
[Extensibility](extensibility.md#replace-the-whole-document).

---

## Passing ambient data

`options.ContextValues` and `httpContext.AddJsxContext(...)` both arrive as the component's
`context` prop, alongside the request `path`:

```tsx
export default function Page({ context }: ViewProps<Model>) {
    return <footer>{String(context.environment)}</footer>;
}
```

See [Extensibility](extensibility.md#pass-ambient-data-to-every-view) for setting them.
