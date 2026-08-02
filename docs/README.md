# JsxCore documentation

A TSX/JSX view engine for ASP.NET Core. To get running, start with
[Getting started](getting-started.md). The rest is reference material to read when you need it.

← [Back to the project README](../README.md)

---

## Getting started

| | |
|---|---|
| **[Getting started](getting-started.md)** | Prerequisites, installation, your first view, project layout |
| **[For frontend developers](for-frontend-developers.md)** | Coming from Next.js or a Vite SPA: what maps onto what, and where server and client code live |
| **[How it works](how-it-works.md)** | The compilation pipeline, native ESM, where the packages come from, build ids, the embedded runtime |

## Using it

| | |
|---|---|
| **[Runtimes](runtimes.md)** | Preact, which ships inside JsxCore, upgrading it, switching to React, and React compatibility |
| **[Render modes](render-modes.md)** | Client, server, or both, and what changes in each |
| **[Writing views](writing-views.md)** | The view contract, component imports, `head`, hooks, the JSX dialect |
| **[Import syntax](import-syntax.md)** | Every import a view can write, and what each one resolves to |
| **[Model types](model-types.md)** | TypeScript types generated from your .NET models, and how to control what is exported |
| **[Views and Web APIs](views-and-web-apis.md)** | A page server rendered for first paint that then talks to your own API |
| **[Returning views](returning-views.md)** | Minimal APIs, MVC controllers, per-response document settings |
| **[Tailwind CSS](tailwind.md)** | Setting it up, and the three things that catch people out |
| **[npm packages](npm-packages.md)** | Importing packages from `node_modules`, on the server and in the browser |
| **[Package management](package-management.md)** | Installing packages without npm, and the `dotnet npm` tool |
| **[.NET interop](dotnet-interop.md)** | Calling .NET objects directly from server-rendered views |

## Operating it

| | |
|---|---|
| **[Development](development.md)** | Hot reload, editor support, and the framework diagnostic header |
| **[Build and deploy](build-and-deploy.md)** | The three build modes, type-checking strictness, minification and compression, publishing without npm |
| **[Testing](testing.md)** | `WebApplicationFactory`, `TestServer`, and the content-root gotcha |

## Reference

| | |
|---|---|
| **[Extensibility](extensibility.md)** | Document templates, ambient context, import maps, view locations, compiler options |
| **[Configuration](configuration.md)** | Every option, and every MSBuild property |
| **[Troubleshooting](troubleshooting.md)** | Common errors, and the limitations to know about up front |
| **[Roadmap](roadmap.md)** | What is not built yet, and what it would involve |

---

## The short version

```bash
dotnet add package JsxCore
```

```csharp
builder.AddJsxCore();
app.UseJsxCore();
app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new { name = "World" }));
```

```tsx
// Views/Home/Index.tsx
export default function Index({ model }: { model: { name: string } }) {
    return <h1>Hello {model.name}</h1>;
}
```

No bundler, no Node.js process, no hand-written model interfaces, and nothing installed but the
.NET SDK.
