# JsxCore

**A TSX/JSX view engine for ASP.NET Core.**

[![Build](https://github.com/davidwhitney/JsxCore/actions/workflows/build.yml/badge.svg)](https://github.com/davidwhitney/JsxCore/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/JsxCore.svg)](https://www.nuget.org/packages/JsxCore)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Native JSX, React and Preact support for ASP.NET MVC, WebAPI and Minimal APIs. JsxCore provides a "vite like" developer experience to the .NET ecosystem.

Write your views as `.tsx` files and return them from a controller or a minimal API. They are real
components — real JSX, rendered by **Preact**, which ships inside the package, or by **React**,
which the build restores for you — running on the server for first paint, in the browser for
interactivity, or both, chosen per response. The model comes from your endpoint, and its TypeScript
type is generated from your C#, so the two cannot drift.

There is no bundler, no Node.js process and no toolchain to assemble. TypeScript 7 is a native
binary JsxCore starts directly, imports are rewritten so the browser walks the module graph itself,
and npm packages are restored by talking to the registry, so `dotnet build` is the entire build.
Save a view and hot reload pushes it over a WebSocket: the browser re-imports that one module and
re-renders in place, falling back to a page reload only when it cannot, and a type error arrives as
an overlay instead of a broken page. `dotnet add package JsxCore` and `dotnet run` is the whole of
the setup, on any machine with the .NET SDK.

- **Views are components.** The same component renders on the server for first paint and SEO, in
  the browser for interactivity, or both, chosen per response.
- **No bundler.** TypeScript rewrites `./Card.tsx` to `./Card.js`, so the browser resolves the
  module graph itself.
- **No Node.js process.** TypeScript 7 ships as a native binary that JsxCore invokes directly, and
  the build installs it for you, so there is no setup step to forget.
- **Real .NET interop.** Server rendering runs in-process, so `.NET` objects exposed to a view are
  real objects, called synchronously with no bridge.
- **npm packages work.** Install a package and import it; it resolves on the server and is served
  to the browser, with no bundler and no import map to write. `dotnet npm add marked` installs one
  without npm on the machine.
- **Preact or React.** Preact ships inside the package, so nothing is installed to render a view.
  One project-file property switches to React, which the build restores for you.
- **Types generated from your C#.** View models are described once, in .NET.
- **Drops into MVC.** Registers as an `IViewEngine`, so `return View()` finds `Index.tsx`.

📖 **[Read the documentation](docs/README.md)**

---

## Install

```bash
dotnet add package JsxCore
```

**The .NET SDK is the only prerequisite.** JsxCore restores the npm packages it needs itself, by
talking to the registry directly, so a clean checkout builds with `dotnet build` on a machine with
no Node and no npm installed. It writes a `package-lock.json` that real `npm ci` accepts, and you
can [switch back to npm](docs/package-management.md#using-npm-instead) with one property if you
would rather. If you already have npm, keep using it: `npm install` works unchanged and JsxCore
reads what it installed.

No Node process runs at build time or when serving a request either: the TypeScript compiler is a
native binary JsxCore starts directly.

**Prerequisites:** .NET 8, 9 or 10. A published application needs nothing else, though views that
import [npm packages](docs/npm-packages.md) still need those package files on the server, which
publish can copy for you.

---

## Quick start: minimal API

```csharp
using JsxCore.Hosting;
using JsxCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.AddJsxCore();

var app = builder.Build();

app.UseJsxCore();

app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new { name = "World" }));

app.Run();
```

```tsx
// Views/Home/Index.tsx
export default function Index({ model }: { model: { name: string } }) {
    return <h1>Hello {model.name}</h1>;
}
```

Run the app.

---

## Quick start: ASP.NET Core MVC

JsxCore registers itself as an `IViewEngine`, so there is nothing JsxCore-specific in the
controller. `View()` finds `Views/Home/Index.tsx` through the normal view location rules.

```csharp
using JsxCore.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.AddJsxCore();

var app = builder.Build();

app.UseJsxCore();
app.MapControllers();

app.Run();
```

```csharp
// Controllers/HomeController.cs
public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View(new IndexModel("World"));
}
```

```csharp
// Models/IndexModel.cs (exported to TypeScript automatically)
namespace MyApp.Models;

public sealed record IndexModel(string Name);
```

```tsx
// Views/Home/Index.tsx
import type { ViewProps } from "@jsxcore/runtime";
import type { MyApp } from "@jsxcore/generated";

export default function Index({ model }: ViewProps<MyApp.Models.IndexModel>) {
    return <h1>Hello {model.name}</h1>;
}
```

Razor keeps working alongside it. A view JsxCore cannot find falls through to Razor, so you can
migrate a page at a time.

---

## Where next

| | |
|---|---|
| **[Getting started](docs/getting-started.md)** | Prerequisites, installation, project layout |
| **[Runtimes](docs/runtimes.md)** | Preact, which ships inside JsxCore, upgrading it, and switching to React |
| **[Render modes](docs/render-modes.md)** | Client, server, or both |
| **[Writing views](docs/writing-views.md)** | The view contract, `head`, hooks, the JSX dialect |
| **[npm packages](docs/npm-packages.md)** | Importing from `node_modules`, on the server and in the browser |
| **[Package management](docs/package-management.md)** | Installing packages without npm, and the `dotnet npm` tool |
| **[Model types](docs/model-types.md)** | TypeScript generated from your .NET models |
| **[.NET interop](docs/dotnet-interop.md)** | Calling .NET directly from server-rendered views |
| **[Build and deploy](docs/build-and-deploy.md)** | Build modes, and publishing without npm |
| **[Full documentation](docs/README.md)** | Everything else |

---

## Sample application

[`samples/SampleApp`](samples/SampleApp) demonstrates every render mode, .NET globals, MVC
integration, generated model types and Preact features:

```bash
dotnet run --project samples/SampleApp
```

[`samples/SampleApp.React`](samples/SampleApp.React) is the smallest thing that works: a minimal
API, one view, and `<JsxCoreFramework>react</JsxCoreFramework>`. The build restores React for it.

```bash
dotnet run --project samples/SampleApp.React
```

---

## Licence

MIT. See [LICENSE](LICENSE).
