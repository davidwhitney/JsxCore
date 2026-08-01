# For frontend developers

← [Documentation index](README.md)

If you have written Next.js, Remix or a Vite SPA, most of what you know transfers. This page maps
what you already do onto how JsxCore does it, and is honest about the parts that are genuinely
different.

The one-sentence version: **your components are the same, your npm packages are the same, and the
thing that used to be `getServerSideProps` is now a C# endpoint.**

---

## The mental model

In Next.js a page file contains both halves of the request — the server work and the component —
separated by a special export. JsxCore splits them by *file* rather than by export: the server half
is a C# endpoint, the client half is your `.tsx`.

```
Next.js                             JsxCore
─────────────────────────           ─────────────────────────
pages/products.tsx                  Program.cs (or a controller)
  getServerSideProps()  ───────►      app.MapGet("/products", ...)
  export default function ─────►    Views/Home/Products.tsx
```

That is the whole difference in shape. Everything below is detail.

### Side by side

A Next.js page that loads data on the server:

```tsx
// pages/products.tsx
export async function getServerSideProps() {
    const products = await db.products.findMany();
    return { props: { products } };
}

export default function Products({ products }) {
    return <ul>{products.map((p) => <li key={p.id}>{p.name}</li>)}</ul>;
}
```

The same page in JsxCore:

```csharp
// Program.cs — this is your getServerSideProps
app.MapGet("/products", (ProductService products) =>
    Results.Extensions.JsxServerRendered("Home/Products", new ProductsModel(products.All())));
```

```tsx
// Views/Home/Products.tsx
import type { ProductsModel } from "dotnet:MyApp/Models";
import type { ViewProps } from "dotnet:rendering";

export default function Products({ model }: ViewProps<ProductsModel>) {
    return <ul>{model.products.map((p) => <li key={p.id}>{p.name}</li>)}</ul>;
}
```

If your application uses controllers instead, the mapping is the same with `View(model)` in place
of the result helper — see below.

Two things worth noticing. The endpoint is an ordinary function with dependency injection, so
`ProductService` arrives as an argument rather than being imported — that is ASP.NET Core's
equivalent of importing your db client. And `ProductsModel` is **generated from the C# record**, so
the props are type-checked against what the server will actually send. Rename a field in C# and the
view stops compiling.

### If the codebase uses MVC controllers

Plenty of ASP.NET Core applications are organised around controllers rather than the minimal APIs
above. Nothing about your `.tsx` changes; only where the server half is written:

```csharp
// Controllers/ProductsController.cs
public sealed class ProductsController(ProductService products) : Controller
{
    [HttpGet("/products")]
    public IActionResult Index() => View(new ProductsModel(products.All()));
}
```

There is nothing JsxCore-specific in that file. JsxCore registers itself as an `IViewEngine`, so the
ordinary `View(model)` call resolves `Views/Products/Index.tsx` — the same view file, receiving the
same `model` prop.

**This is the closest thing to file-system routing here.** A controller action looks for its view by
convention, in order:

```
Views/{controller}/{action}.tsx      →  Views/Products/Index.tsx
Views/Shared/{action}.tsx            →  Views/Shared/Index.tsx
Views/{action}.tsx                   →  Views/Index.tsx
```

Areas work too, and are tried first when the request has one. Returning `View("Other")` picks a
different view by name, exactly as it would with Razor.

Render mode is per action rather than per result, set through `ViewData`:

```csharp
[HttpGet("/products")]
public IActionResult Index()
{
    ViewData[JsxViewEngine.RenderModeKey] = RenderMode.ServerAndClient;
    return View(new ProductsModel(products.All()));
}
```

**Razor keeps working beside it.** A view JsxCore cannot find falls through to the Razor view engine,
so an existing application can move one page at a time: rename `Index.cshtml` to `Index.tsx`, write
it as a component, and leave the controller alone.

---

## Where code runs

Next.js decides with `"use client"`, server components and the file's exports. JsxCore decides
**per response**, at the endpoint, and there are three answers:

| Result | Component runs | JavaScript sent | Closest Next.js idea |
|---|---|---|---|
| `Results.Extensions.Jsx(...)` | browser only | yes | a client component, data fetched server-side and serialised |
| `Results.Extensions.JsxServerRendered(...)` | server only | none | a server component: markup, no hydration |
| `Results.Extensions.JsxServerAndClient(...)` | both | yes | the classic SSR + hydrate page |

The component file is identical in all three. There is no directive at the top and no separate
server bundle: the same compiled module is loaded by the browser and by the server renderer.

### What that means when you write a component

**Server rendering is synchronous.** There is no `await` in a component, because there is nothing to
await — the data is already in the model your endpoint built. An `async` component is rejected with
an explicit error rather than silently rendering nothing.

**Anything browser-only has to be guarded**, exactly as it does with SSR anywhere:

```tsx
import { isServerRender } from "dotnet:rendering";

if (!isServerRender()) {
    const { default: chart } = await import("chart.js");   // dynamic import, client only
}
```

**Hooks work as you expect.** `useState`, `useEffect` and the rest come from `preact/hooks` or, in
React mode, from `react`. During the server pass hooks return their initial values and effects never
run, which is standard SSR behaviour.

**`head` replaces `next/head`:**

```tsx
export const head = (model: ProductsModel) => ({
    title: `${model.category} | Shop`,
    meta: [{ name: "description", content: model.summary }]
});
```

It is evaluated even for client-rendered views, so the document gets a real title without shipping
the page first.

### Calling the server from inside a view

If you want the `getServerSideProps` feeling — reaching a service from within the view — you can,
during the server pass:

```tsx
import { Inventory } from "dotnet:globals";
import { isServerRender } from "dotnet:rendering";

const total = isServerRender() ? Inventory.getTotalValue() : null;
```

`Inventory` is a .NET object you registered, typed from its C#. The call is a direct in-process
method call, not a fetch: there is no HTTP hop, no serialisation and nothing to await. Using it from
a client-rendered view throws with an explanation.

Most of the time, passing the data in the model is the better shape. This is for the cases where a
deep component needs something the endpoint did not know to fetch.

---

## Your npm packages just work

```bash
npm install marked
```

```tsx
import { marked } from "marked";
```

That is the whole story. JsxCore reads `package.json`, resolves what your views import, and
generates the browser's [import map](import-syntax.md) so the same specifier resolves on both sides.

- **There is no bundler.** Modules are served as modules; the browser walks the import graph.
- **`dependencies` reach the browser; `devDependencies` do not**, which is the same cut a production
  `npm ci` makes. Importing a devDependency from a client-rendered view logs a warning rather than
  failing in production.
- **CommonJS packages work**, wrapped automatically. A default import is the reliable form.
- **You do not need Node installed.** JsxCore talks to the npm registry itself and writes a
  `package-lock.json` that real `npm ci` accepts. If you already have npm, keep using it — nothing
  here objects.

For a Release build, everything served is minified and compressed, and the publish output carries
your dependencies and theirs. You do not configure any of that. See
[Build and deploy](build-and-deploy.md#minification-and-compression).

---

## The workflow

| You want | Next.js | JsxCore |
|---|---|---|
| Dev server with fast refresh | `next dev` | `dotnet run` — views hot reload over a WebSocket |
| Restart on server-code change | automatic | `dotnet watch run` |
| Production build | `next build` | `dotnet publish -c Release` |
| Type errors | `tsc --noEmit` | reported by the build, and in a dev overlay in the page |
| Add a package | `npm install x` | `npm install x`, or `dotnet npm add x` |

Editing a `.tsx` recompiles and re-imports just that module — no page reload. Editing C# needs
`dotnet watch`, because that is a .NET rebuild rather than a JavaScript one.

---

## What is different

Worth knowing before you commit:

- **No file-system routing.** A view is not a route; an endpoint chooses a view by name. That is
  more typing for a simple page and considerably less for anything with real routing, auth or
  content negotiation, because it is all just ASP.NET Core.
- **No async components, and no streaming or React Server Components.** Server rendering is
  synchronous. Fetch in the endpoint.
- **No `next/image`, `next/link` or `next/font`.** Plain HTML, plus `UseStaticFiles()`.
- **No API routes to write**, because you are already in the API. The endpoint that renders the page
  and the endpoint that returns JSON are the same kind of thing.
- **One process, one deployment.** No Node server beside your .NET one, and nothing to keep in sync
  between them.
- **Types come from C#.** Instead of hand-writing an interface that mirrors your API response, the
  model type is generated from the type your endpoint actually returns.

---

## Where to go next

- [Getting started](getting-started.md) — install it and serve a view
- [Render modes](render-modes.md) — client, server, or both, in detail
- [Writing views](writing-views.md) — the view contract, `head`, hooks, the JSX dialect
- [Import syntax](import-syntax.md) — every import a view can write
- [Views and Web APIs](views-and-web-apis.md) — server render first paint, then fetch from your own API
- [Returning views](returning-views.md) — minimal APIs and MVC controllers, per-response settings
- [.NET interop](dotnet-interop.md) — registering the objects behind `dotnet:globals`
