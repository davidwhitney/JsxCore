# For frontend developers

← [Documentation index](README.md)

If you have written Next.js, Remix or a Vite SPA, most of what you know transfers. This page maps
what you already do onto how JsxCore does it, and links to the detail.

The one-sentence version: **your components are the same, your npm packages are the same, and the
thing that used to be `getServerSideProps` is now a C# endpoint.**

---

## The mental model

In Next.js a page file contains both halves of the request, the server work and the component,
separated by a special export. JsxCore splits them by *file* rather than by export: the server half
is a C# endpoint, the client half is your `.tsx`.

```
Next.js                             JsxCore
─────────────────────────           ─────────────────────────
pages/products.tsx                  Program.cs (or a controller)
  getServerSideProps()  ───────►      app.MapGet("/products", ...)
  export default function ─────►    Views/Home/Products.tsx
```

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
// Program.cs: this is your getServerSideProps
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

The endpoint is an ordinary function with dependency injection, so `ProductService` arrives as an
argument rather than being imported. `ProductsModel` is **generated from the C# record**, so the
props are checked against what the server will actually send: rename a field in C# and the view
stops compiling.

Controllers work the same way: `return View(model)` resolves `Views/Products/Index.tsx` with no
JsxCore-specific code in the action. See
[Returning views](returning-views.md#mvc-controllers).

---

## Where each thing lives

| What you would reach for | Here | Detail |
|---|---|---|
| `getServerSideProps` | a C# endpoint, or a controller action | [Returning views](returning-views.md) |
| `"use client"` / server components | a render mode, chosen per response | [Render modes](render-modes.md) |
| File-system routing | `app.MapGet(...)`, or controller conventions | [Returning views](returning-views.md#view-resolution) |
| `next/head` | the `head` export | [Writing views](writing-views.md#the-head-export) |
| Hooks | `preact/hooks`, or `react` in React mode | [Writing views](writing-views.md#hooks) |
| `npm install x` | `npm install x`, or `dotnet npm add x` | [npm packages](npm-packages.md) |
| Importing an image or a stylesheet | `dotnet:wwwroot/…` | [Import syntax](import-syntax.md#static-assets) |
| An API route | you are already in the API | [Views and Web APIs](views-and-web-apis.md) |
| Hand-written response interfaces | generated from your C# | [Model types](model-types.md) |
| `next dev` | `dotnet run`, with hot reload over a WebSocket | [Development](development.md) |
| `next build` | `dotnet publish -c Release` | [Build and deploy](build-and-deploy.md) |

---

## What is different

- **No file-system routing.** A view is not a route; an endpoint chooses a view by name. More
  typing for a simple page, considerably less for anything with real routing, auth or content
  negotiation, because it is all just ASP.NET Core.
- **No async components, streaming or React Server Components.** Server rendering is synchronous;
  the endpoint that builds the model is [ordinary async C#](returning-views.md#async-endpoints).
- **No `next/image`, `next/link` or `next/font`.** Plain HTML, plus `UseStaticFiles()`.
- **Assets live in `wwwroot`, not beside the component.** ASP.NET Core serves them; nothing is
  bundled or fingerprinted. CSS Modules are [not built yet](roadmap.md).
- **No API routes to write**, because the endpoint that renders the page and the endpoint that
  returns JSON are the same kind of thing.
- **One process, one deployment.** No Node server beside your .NET one.
- **Types come from C#.** The model type is generated from the type the endpoint returns, rather
  than hand-written to mirror it.

Some of that is a genuine trade. Losing file-system routing is a real cost if your application is
mostly pages; gaining one process, one router and one type definition is a real saving if it is
mostly an application.

---

## Where to go next

- [Getting started](getting-started.md): install it and serve a view
- [Writing views](writing-views.md): the view contract, `head`, hooks, the JSX dialect
- [Render modes](render-modes.md): client, server, or both
- [Views and Web APIs](views-and-web-apis.md): server render for first paint, then fetch
- [.NET interop](dotnet-interop.md): calling a registered service during the server pass
