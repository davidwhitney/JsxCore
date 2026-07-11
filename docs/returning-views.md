# Returning views

← [Documentation index](README.md)

---

## Minimal APIs

```csharp
app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", model));
app.MapGet("/report", () => Results.Extensions.JsxServerRendered("Home/Report", model));
app.MapGet("/search", () => Results.Extensions.JsxServerAndClient("Home/Search", model));
```

Or with an explicit mode:

```csharp
Results.Extensions.Jsx("Home/Index", model, RenderMode.Server);
```

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

One limitation worth knowing: an IDE cannot see run time configuration, so an application that
changes `options.ViewLocationFormats` in code gets annotations describing the defaults. Set
`JsxCoreViewLocationFormats` in the project file to match if you have moved them.

Be explicit when you want a particular mode:

```csharp
public IActionResult Report() => this.JsxServerRendered("Home/Report", model);
public IActionResult Search() => this.JsxServerAndClient("Home/Search", model);
public IActionResult Index()  => this.Jsx("Home/Index", model);
```

### View resolution

View names resolve through `ViewLocationFormats`, which default to:

```
{ViewsDirectory}/{controller}/{view}
{ViewsDirectory}/Shared/{view}
{ViewsDirectory}/{view}
```

with `.tsx` tried before `.jsx`. Explicit paths work too:

```csharp
this.Jsx("Home/Index", model);              // views-directory relative
this.Jsx("~/Views/Home/Index.tsx", model);  // content-root relative
```

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

Values that every view should see, configured once:

```csharp
options.ContextValues["environment"] = builder.Environment.EnvironmentName;
```

Values for one request:

```csharp
httpContext.AddJsxContext("user", user.Identity?.Name);
```

Both arrive as the component's `context` prop, alongside the request `path`:

```tsx
export default function Page({ context }: ViewProps<Model>) {
    return <footer>{String(context.environment)}</footer>;
}
```
