<img src="https://raw.githubusercontent.com/davidwhitney/JsxCore/main/images/jsxcore-banner-final.png"
     alt="JsxCore: JSX and React, natively in ASP.NET Core" width="820">

# JsxCore

**A TSX/JSX view engine for ASP.NET Core.**

[![Build](https://github.com/davidwhitney/JsxCore/actions/workflows/build.yml/badge.svg)](https://github.com/davidwhitney/JsxCore/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/JsxCore.svg)](https://www.nuget.org/packages/JsxCore)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Native JSX, React, Preact and TypeScript support for ASP.NET MVC, WebAPI and Minimal APIs. JsxCore provides a "vite-like" developer experience to the .NET ecosystem.

**This makes ASP.NET Core a fully featured React and TypeScript developer experience, comparable and competitive with Node-based frameworks like Next.js, Remix, and Astro, but without the Node runtime or npm dependency.**

Sold already? Go [get started](docs/getting-started.md) or [install the package](#install).

## Comprehensive Documentation

* **[Getting started](docs/getting-started.md)**
* **[Read the full documentation](docs/README.md)**

## Want to know more?

Write your views as `.tsx` files and return them from a controller or a minimal API. They are real
components — real JSX, rendered by **Preact**, which ships inside the package, or by **React**,
which the build restores for you — running on the server for first paint, in the browser for
interactivity, or both, chosen per response. The model comes from your endpoint, and its TypeScript
type is generated from your C#, so the two cannot drift.

Key Features:

- **Use React or Preact** as ASP.NET Core Views
- **Zero configuration**: the package handles sourcing TypeScript compilers and esbuild minifiers automatically, so there is no setup step to forget.
- **Views are components.** The same component renders on the server for first paint and SEO, in
  the browser for interactivity, or both, chosen per response.
- **No bundler.** TypeScript rewrites `./Card.tsx` to `./Card.js`, so the browser resolves the
  module graph itself.
- **Real .NET interop.** Server rendering runs in-process, so `.NET` objects exposed to a view are
  real objects, called synchronously with no bridge.
- **npm packages work.** Install a package and import it; it resolves on the server and is served
  to the browser, with no bundler and no import map to write. `dotnet npm add marked` installs one
  without npm on the machine.
- **Idiomatic .NET** Preact ships inside the package, so nothing is installed to render a view.
  One project-file property switches to React, which the build restores for you.
- **Types generated from your C#.** View models are described once, in .NET.
- **Drops into MVC.** Registers as an `IViewEngine`, so `return View()` finds `Index.tsx`.

## Install

```bash
dotnet add package JsxCore
```

**The .NET SDK is the only prerequisite.**

JsxCore restores the npm packages it needs itself, by
talking to the registry directly, so a clean checkout builds with `dotnet build` on a machine with
no Node and no npm installed.

**Prerequisites:** .NET 8, 9 or 10. Views that
import [npm packages](docs/npm-packages.md) still need those package files on the server, which
publish will copy for you.

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

## Sample apps included


[`samples/SampleApp`](samples/SampleApp) demonstrates every render mode, .NET globals, MVC
integration, generated model types and Preact features:

```bash
dotnet run --project samples/SampleApp
```

You'll see **a view mounted in the browser**, from a model serialised by your endpoint:

<img src="https://raw.githubusercontent.com/davidwhitney/JsxCore/main/images/screenshots/sample-client.png" alt="A client-rendered JsxCore view, with a working counter" width="820">

**Calling .NET from a server-rendered view.** No fetch, no API, no bridge: the component asked a C#
service for the total during rendering, and the browser received markup:

<img src="https://raw.githubusercontent.com/davidwhitney/JsxCore/main/images/screenshots/sample-dotnet-globals.png" alt="A server-rendered view reading values from a .NET service" width="820">

**A type error while you work.** The watcher recompiles on save and pushes the diagnostics to the
page, rather than serving a stale build or a blank screen:

<img src="https://raw.githubusercontent.com/davidwhitney/JsxCore/main/images/screenshots/hot-reload-error.png" alt="The development overlay showing a TypeScript compilation error" width="820">

## Licence

MIT. See [LICENSE](LICENSE).
