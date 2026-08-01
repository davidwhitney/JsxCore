# Render modes

← [Documentation index](README.md)

Every response chooses where its component runs.

---

## The three modes

| Mode | Component runs | JavaScript sent | .NET globals | Use for |
|---|---|---|---|---|
| `Client` *(default)* | Browser | Yes | No | Interactive pages |
| `Server` | Server | None | Yes | Content pages, email, SEO, no-JS |
| `ServerAndClient` | Both | Yes | Server pass only | First paint **and** interactivity |

```csharp
Results.Extensions.Jsx("Home/Index", model);                  // Client
Results.Extensions.JsxServerRendered("Home/Report", model);   // Server
Results.Extensions.JsxServerAndClient("Home/Search", model);  // Both
```

Change the default for the whole application:

```csharp
builder.AddJsxCore(options => options.DefaultRenderMode = RenderMode.Server);
```

From an MVC controller, set it per view with the extension methods, or via ViewData:

```csharp
ViewData[JsxViewEngine.RenderModeKey] = RenderMode.Server;
return View(model);
```

---

## Client

The server emits an HTML shell: the document head, an import map, the serialised model in a
`<script type="application/json">` tag, and a module script that mounts the component.

```html
<div id="jsxcore-root"></div>
<script type="application/json" id="jsxcore-model">{"name":"World"}</script>
<script type="module">
import Component from "/_jsx/vabc123/views/Home/Index.js";
import { mountView } from "dotnet:rendering/client";
window.__jsxcore_context = {"path":"/"};
mountView(Component, {"containerId":"jsxcore-root","modelId":"jsxcore-model","hydrate":false});
</script>
```

The component never runs on the server, but its [`head` export](writing-views.md#the-head-export)
still does, so the document gets its title and meta tags without you doing anything.

`.NET` globals are unavailable. Accessing them throws a clear error rather than failing silently.

---

## Server

The component runs in the embedded JavaScript engine and its markup is written straight into the
response. **No JavaScript is sent to the browser at all** (except the hot reload client in
development).

This is the traditional view engine mode: good for content pages, pages that must work without
JavaScript, anything crawled by a search engine, and HTML email.

[.NET globals](dotnet-interop.md) are available, and hooks return their initial values: `useState`
gives you the initial state, and effects never run. That is standard SSR behaviour.

---

## ServerAndClient

The markup is produced on the server for first paint, then the same component is mounted in the
browser so the interactive parts come alive.

This is true hydration: the existing DOM nodes are reused and only event handlers are attached,
rather than the server's markup being thrown away and rebuilt.

### Your component runs twice

Once on the server, once on the client. Anything server-only has to be guarded:

```tsx
import { isServerRender } from "dotnet:rendering";

export default function Page({ model }: ViewProps<Model>) {
    // Only exists during the server pass.
    const extra = isServerRender() ? dotnet.Inventory.getSummary() : null;

    return <p>Running on the {isServerRender() ? "server" : "client"}.</p>;
}
```

If the two passes produce different markup, the client render wins, but a mismatch means hydration
has to repair the DOM, which costs more than getting it right. Keep the
first render deterministic: no `Date.now()`, no `Math.random()`, no reading `window`.

---

## Choosing

- **Mostly static content?** `Server`. Nothing to ship, nothing to hydrate.
- **An app-like page behind a login?** `Client`. First paint matters less than interactivity.
- **A public page that also needs to be interactive?** `ServerAndClient`.

There is no global right answer, which is why it is per response.
