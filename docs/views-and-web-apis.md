# Views and Web APIs

← [Documentation index](README.md)

A common shape: a page that is **server rendered for first paint** and then **talks to a Web API**
for everything after it. Both halves live in the same ASP.NET Core application, share the same
types, and need no extra infrastructure between them.

This page builds one end to end.

---

## The rule that decides the design

**A server-rendered view cannot call an API.** Server rendering runs in an embedded JavaScript
engine, synchronously: there is no `fetch`, no `await`, and a component that returns a Promise is
rejected with an explicit error.

That forces the question *who fetches what* to have one answer:

| When | Who fetches | How |
|---|---|---|
| First paint | your controller | `HttpClient`, or just the service directly |
| After hydration | the component | `fetch`, in an effect or an event handler |

The component never has to ask which it is doing. Effects do not run during server rendering, so
an effect-based fetch is *automatically* client-only.

---

## The API

An ordinary Web API controller. Nothing here knows a view exists:

```csharp
// Controllers/ProductsApiController.cs
[ApiController]
[Route("api/products")]
public sealed class ProductsApiController(ProductService products) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<Product>> Get() => Ok(products.All());
}
```

```csharp
// Models/Product.cs
namespace MyApp.Models;

public sealed record Product(int Id, string Name, decimal Price, int Quantity);
```

---

## The page

The MVC action does the first fetch. Because the API is in the same application, the honest thing is
usually to call the service rather than to make an HTTP request to yourself:

```csharp
// Controllers/ProductsController.cs
public sealed class ProductsController(ProductService products) : Controller
{
    [HttpGet("/products")]
    public IActionResult Index()
    {
        // Server rendered, then hydrated: markup for first paint, then interactive.
        ViewData[JsxViewEngine.RenderModeKey] = RenderMode.ServerAndClient;

        return View(new ProductsModel(products.All()));
    }
}
```

If the API genuinely is somewhere else, the action is where the HTTP call belongs, and it can be
`async` because C# can:

```csharp
public sealed class ProductsController(IHttpClientFactory factory) : Controller
{
    [HttpGet("/products")]
    public async Task<IActionResult> Index()
    {
        var client = factory.CreateClient("catalogue");
        var products = await client.GetFromJsonAsync<Product[]>("api/products") ?? [];

        ViewData[JsxViewEngine.RenderModeKey] = RenderMode.ServerAndClient;
        return View(new ProductsModel(products));
    }
}
```

---

## The view

One component, rendered twice: on the server with the model, then in the browser where it can fetch.

```tsx
// Views/Products/Index.tsx
import { useState, useEffect } from "preact/hooks";
import type { ViewProps } from "dotnet:rendering";
import type { ProductsModel, Product } from "dotnet:types/MyApp/Models";

export const head = { title: "Products" };

export default function Index({ model }: ViewProps<ProductsModel>) {
    // Seeded from the server render, so the first paint is complete markup.
    const [products, setProducts] = useState<Product[]>(model.products);
    const [refreshing, setRefreshing] = useState(false);

    // Effects never run during server rendering, so this is the client half by construction.
    useEffect(() => {
        const timer = setInterval(async () => {
            setRefreshing(true);
            const response = await fetch("/api/products");
            setProducts(await response.json());
            setRefreshing(false);
        }, 10_000);

        return () => clearInterval(timer);
    }, []);

    return (
        <main>
            <h1>Products {refreshing ? <small>updating…</small> : null}</h1>
            <ul>
                {products.map((product) => (
                    <li key={product.id}>{product.name}: {product.price.toFixed(2)}</li>
                ))}
            </ul>
        </main>
    );
}
```

**`Product` is the same type on both sides.** The API returns the C# record; the generated
declaration describes it; the view uses it for the model *and* for the `fetch` response. There is no
hand-written interface mirroring the JSON, and no way for the two to drift: rename a property in C#
and the view stops compiling.

That is the part worth stealing from this design even if you use nothing else: the API contract is
checked at compile time, in both languages, from one definition.

---

## What the browser actually receives

1. HTML containing the rendered list, so the page is complete before any JavaScript runs.
2. The serialised model in a `<script type="application/json">` tag.
3. A module script that hydrates the same component over the existing DOM.
4. Later, whatever `fetch` returns: plain JSON from your API, no framework involved.

Same-origin, so no CORS and no second host. The API endpoint and the page endpoint are the same kind
of thing in the same application, sharing DI, authentication and configuration.

---

## Variations

**Client rendered, fetch for everything.** Drop the server pass and let the component load its own
data. Use `Results.Extensions.Jsx(...)` or leave the default render mode alone, pass a small model
(or none), and fetch in an effect. You lose first-paint markup and SEO; you gain a simpler endpoint.

**Server rendered, no JavaScript at all.** Open the view with `"use server"` and it sends markup and
no script. Good for content pages, email and anything crawled. There is no client half to write.

**Fetching on an event rather than a timer.** Exactly as above, in a handler:

```tsx
const search = async (term: string) => {
    const response = await fetch(`/api/products?q=${encodeURIComponent(term)}`);
    setProducts(await response.json());
};
```

**Reading .NET during the server pass.** If a deep component needs something the endpoint did not
fetch, [`dotnet:globals`](dotnet-interop.md) reaches a registered service directly, as an in-process
call rather than an HTTP one. Guard it with `isServerRender()`.

---

## Things that will bite

- **`fetch` does not exist during server rendering.** Calling it in a component body rather than an
  effect fails on the server pass. Effects are the safe place.
- **Hydration mismatch.** If the first client render produces different markup from the server's,
  the DOM has to be repaired. Seed state from the model, as above, rather than fetching immediately
  on mount and rendering an empty list first.
- **Two round trips if you fetch on mount.** The server already has the data; passing it in the
  model means the page is useful before the API is called at all.
- **`async` components are rejected.** The error says so explicitly. Await in the endpoint (see
  [async endpoints](returning-views.md#async-endpoints)), or fetch in an effect.

---

## See also

- [For frontend developers](for-frontend-developers.md): the Next.js mapping, and MVC controllers
- [Render modes](render-modes.md): client, server, or both, in detail
- [Returning views](returning-views.md): minimal APIs, controllers, per-response settings
- [.NET interop](dotnet-interop.md): calling .NET directly during the server pass
- [Model types](model-types.md): how the shared types are generated
