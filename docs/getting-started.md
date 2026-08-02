# Getting started

← [Documentation index](README.md)

Prerequisites, installing the package, your first view, and what the project looks like
afterwards.

---

## Prerequisites

| | Requirement | Why |
|---|---|---|
| **.NET** | 8.0, 9.0 or 10.0 | The package targets all three |
| **TypeScript 7+** | installed for you on first run | Compiles views |
| **A JS framework** | Preact ships inside JsxCore; React is restored for you | See [Runtimes](runtimes.md) |

**Node and npm are not required.** See
[package management](package-management.md) for how it works, the `dotnet npm` tool, and how to
use npm instead if you would rather.

The TypeScript compiler is a standalone native executable
that JsxCore starts directly, and Preact is a set of files carried inside the JsxCore package. There
is no Node process at build time or when serving a request.

---

## Installation

```bash
dotnet add package JsxCore
```

The build creates a
`package.json` if there is not one already:

```
JsxCore: no package.json found, creating one in /src/MyApp.
JsxCore: restoring with native: typescript
JsxCore: fetching @typescript/typescript-linux-x64@7.0.2
```

This happens during `dotnet build`, `dotnet publish` and `dotnet run` alike, so a clean checkout
and a build agent are both covered.

[Package management](package-management.md) covers the mechanics, and how to take them over.

---

## Your first view

Register the view engine and serve a view:

```csharp
using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.AddJsxCore();          // verifies the toolchain; throws if it is missing

var app = builder.Build();
app.UseJsxCore();              // serves compiled modules, and hot reload in development

app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new { name = "World" }));

app.Run();
```

Create `Views/Home/Index.tsx`:

```tsx
export default function Index({ model }: { model: { name: string } }) {
    return <h1>Hello {model.name}</h1>;
}
```

Run it.

### Order matters in the pipeline

Call `UseJsxCore()` **before** `UseRouting()`, so requests for compiled modules short-circuit
before routing has to consider them. It also adds `UseWebSockets()` for you when hot reload is on.

---

## Project layout

A typical application looks like this:

```
MyApp/
├── Models/                    ← exported to TypeScript automatically
│   └── IndexModel.cs
├── Views/
│   ├── tsconfig.json          ← generated, commit it (editor support)
│   ├── Shared/
│   │   └── Layout.tsx
│   └── Home/
│       └── Index.tsx
├── obj/JsxCore/               ← generated, gitignored
│   ├── js/                    ← compiled views
│   ├── types/MyApp.d.ts       ← types generated from your .NET models
│   └── tsconfig.json          ← the config the compiler actually uses
├── package.json               ← typescript, esbuild, and React if you selected it
└── Program.cs
```

Everything JsxCore generates lives under `obj/`, which the standard .NET `.gitignore` already
covers, with one exception: `Views/tsconfig.json`, which exists so editors can resolve imports and
is worth committing. See [Development](development.md#editor-support).

---

## Startup verification

`AddJsxCore()` verifies the environment **synchronously, at registration**, and throws
`JsxCoreEnvironmentException` if anything it needs is missing.

It checks that a TypeScript compiler is present and new enough, that the views directory exists,
and that the working directory is writable. A failure tells you what is missing, every path it
looked in, and the command that fixes it:

```
JsxCore could not find the TypeScript compiler, which it needs to compile .tsx and .jsx views.

Build the project to install it, which needs no other tooling, or install it yourself:

    npm install --save-dev typescript@^7

JsxCore looks for the native compiler shipped by that package (@typescript/typescript-linux-x64).

Paths searched:
    /app/node_modules/@typescript/typescript-linux-x64/lib/tsc
    /node_modules/@typescript/typescript-linux-x64/lib/tsc
```

---

## Where to go next

- [Package management](package-management.md): adding npm packages with `dotnet npm add`
- [Runtimes](runtimes.md): Preact ships inside JsxCore, how to upgrade it, and switching to React
- [Render modes](render-modes.md): client, server, or both
- [Model types](model-types.md): stop hand-writing TypeScript interfaces for your view models
- [How it works](how-it-works.md): why there is no bundler
