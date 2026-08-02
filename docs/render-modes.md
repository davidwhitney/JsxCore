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

---

## A view can say where it runs

Open a view with a directive and it renders that way wherever it is returned from:

```tsx
"use server";

import type { ViewProps } from "dotnet:rendering";
import type { ReportModel } from "dotnet:MyApp/Models";

export default function Report({ model }: ViewProps<ReportModel>) {
    return <table>{/* ... */}</table>;
}
```

```tsx
"use client";

import { useState } from "preact/hooks";

export default function Search() {
    const [query, setQuery] = useState("");
    return <input value={query} onInput={(e) => setQuery(e.currentTarget.value)} />;
}
```

The endpoint then says nothing about rendering:

```csharp
app.MapGet("/report", () => Results.Extensions.Jsx("Home/Report", model));
app.MapGet("/search", () => Results.Extensions.Jsx("Home/Search", model));
```

A report is markup whoever asks for it, and a search box is interactive whoever asks for it.
Repeating that at every call site is how the two drift.

The directive has to be the **first statement in the file**, before the imports, in the position
JavaScript reserves for `"use strict"`. Comments above it are fine, and either quote style works.

### It applies to the view, not to what it imports

Only the view the endpoint named is consulted. A directive at the top of `Shared/Card.tsx` says
nothing about the pages that import it.

That differs from Next.js, where `"use client"` marks a bundling boundary that propagates. JsxCore
has one module graph and one mode per response, so propagation would mean a shared component
quietly changing how every page importing it renders.

---

## Choosing at the endpoint instead

Pass a mode and it wins, whatever the view says:

```csharp
app.MapGet("/search", () => Results.Extensions.Jsx("Home/Search", model, RenderMode.ServerAndClient));
```

`ServerAndClient` has no directive of its own, because it is genuinely a per-response decision: the
same view is often server-rendered on a public page and client-only behind a login.

From an MVC controller, through `ViewData`:

```csharp
ViewData[JsxViewEngine.RenderModeKey] = RenderMode.ServerAndClient;
return View(model);
```

And the fallback for a view that declares nothing and an endpoint that asks for nothing:

```csharp
builder.AddJsxCore(options => options.DefaultRenderMode = RenderMode.Server);
```

### The order

**The mode the endpoint passed** → **the view's directive** → **`options.DefaultRenderMode`**

Each is more specific than the next. An endpoint naming a mode is deciding for one response and
knows why; a directive is the view stating where it expects to run, which holds until an endpoint
says otherwise.

The directive is read from the compiled output, which the build records once, so it costs nothing
per request and works on a server published with no `.tsx` files on it. Changing one takes effect
on the next build.

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
still does, so the document gets its title and meta tags in the first response. A
[`<Head>`](writing-views.md#the-head-component) inside the component is applied by the browser
instead, once it has mounted.

`.NET` globals are unavailable. Accessing them throws a clear error rather than failing silently.

---

## Server

The component runs in the embedded JavaScript engine and its markup is written straight into the
response. **No JavaScript is sent to the browser at all** (except the hot reload client in
development).

This is the traditional view engine mode: good for content pages, pages that must work without
JavaScript, anything crawled by a search engine, and HTML email.

[.NET globals](dotnet-interop.md) are available, and hooks return their initial values: `useState`
gives the initial state, and effects never run. That is standard SSR behaviour.

The component runs synchronously, so anything needing `await` happens in the endpoint that built the
model. See [async endpoints](returning-views.md#async-endpoints).

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
import { Inventory } from "dotnet:globals";

export default function Page({ model }: ViewProps<Model>) {
    // Only reachable during the server pass.
    const extra = isServerRender() ? Inventory.getSummary() : null;

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
