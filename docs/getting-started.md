# Getting started

← [Documentation index](README.md)

---

## Prerequisites

| | Requirement | Why |
|---|---|---|
| **.NET** | 8.0, 9.0 or 10.0 | The package targets all three |
| **TypeScript 7+** | installed for you on first run | Compiles views |
| **A JS framework** | Preact ships inside JsxCore; React is restored for you | See [Runtimes](runtimes.md) |

**Node and npm are not required.** JsxCore restores those packages itself, talking to the npm
registry directly, and writes a `package-lock.json` that real npm accepts. See
[package management](package-management.md) for how it works, the `dotnet npm` tool, and how to
use npm instead if you would rather.

No JavaScript tooling runs on Node either: the TypeScript compiler is a standalone native executable
that JsxCore starts directly, and Preact is a set of files carried inside the JsxCore package. There
is no Node process at build time or when serving a request.

Restoring is checked on every build, not just the first. Each build checks that everything
`package.json` declares is actually in `node_modules` and restores what is not, because a declared
but uninstalled package produces the worst kind of failure: the build succeeds and the view fails to
render later. A build where nothing is missing does no work at all.

---

## Installation

```bash
dotnet add package JsxCore
```

That is all you have to run. The build installs the packages JsxCore needs, and creates a
`package.json` if there is not one already:

```
JsxCore: no package.json found, creating one in /src/MyApp.
JsxCore: restoring with native: typescript
JsxCore: fetching @typescript/typescript-linux-x64@7.0.2
```

This happens during `dotnet build`, `dotnet publish` and `dotnet run` alike, so a clean checkout
and a build agent are both covered. Nothing runs once the packages are present.

If you would rather manage packages yourself:

```bash
dotnet npm add typescript --version ^7 --dev    # or: npm install --save-dev typescript@^7
```

See [package management](package-management.md#on-the-command-line) for that tool.

JsxCore searches for the compiler in `node_modules` starting at the content root and walking
upwards, so a solution-level install works too. If your content root sits outside the npm project
(common under test hosts), point it at the right place:

```csharp
options.AdditionalToolchainSearchPaths.Add(repositoryRoot);
// or
options.TypeScriptCompilerPath = "/path/to/node_modules/@typescript/typescript-linux-x64/lib/tsc";
```

### Automatic dependency installation

There are two places this can happen, with different rules.

**During the build**, which is the one that normally fires, because `dotnet run` builds first:

| | |
|---|---|
| Any configuration | Not restricted to Development; a build agent needs this to work |
| Only when the compiler is missing | Nothing runs on a build where the packages are present |
| The lock file when there is one | Restores exactly what is pinned and does not rewrite `package-lock.json` |
| Resolving afresh only as a fallback | When there is no lock file, or restoring from it did not produce a compiler |
| Never fails the build | It warns with `JSX0001` and leaves compilation to startup |

Turn it off with `<JsxCoreAutoInstallDependencies>false</JsxCoreAutoInstallDependencies>`. Packages
are restored by JsxCore itself; `<JsxCoreUseNpm>true</JsxCoreUseNpm>` hands the job to the npm on
your machine instead, and `<JsxCoreNpm>` says which npm that is.

**At application startup**, as a fallback for an application launched from prebuilt output where
the package's build targets never ran:

| | |
|---|---|
| Development only | The default is `DependencyInstallMode.Development`. A published application never installs anything |
| Never when precompiled | `PrecompiledOnly` skips it entirely; there is nothing to install and no reason to write to a server's disk |
| Only what is missing | Nothing runs on subsequent starts |
| Reported as it happens | Every command is printed before it runs |
| Never fatal on its own | If the install fails, startup still gives you the command to run by hand |

```csharp
builder.AddJsxCore(options => options.AutoInstallDependencies = DependencyInstallMode.Never);
```

Other settings: `PackageManager` to name a strategy, `NpmPath`, `DependencyInstallTimeout`, and
`OnBootstrapMessage` to route progress somewhere other than the console.

Both paths install `typescript` as a dev dependency. Preact needs nothing installed, because it
ships inside the JsxCore package; [React mode](runtimes.md) adds `react` and `react-dom` as regular
dependencies, and their `@types` packages as dev ones. Packages go in the `package.json` beside the
project file, and one is created there if it does not exist. That directory is not searched upwards
from — an unrelated manifest in a parent or home directory is not adopted — so a solution sharing a
single manifest says so with `<JsxCoreManifestDirectory>`.

Commit the generated `package.json` and `package-lock.json`: they pin your versions, and they let
the build restore exactly what is pinned rather than resolving afresh. The lock file is a standard
`lockfileVersion` 3 file, so npm, Dependabot and Renovate all read it.

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
│   ├── types/index.d.ts       ← types generated from your .NET models
│   └── tsconfig.json          ← the config the compiler actually uses
├── package.json               ← typescript, and React if you selected it
└── Program.cs
```

Everything JsxCore generates lives under `obj/`, which the standard .NET `.gitignore` already
covers, with one exception: `Views/tsconfig.json`, which exists so editors can resolve imports and
is worth committing. See [Development](development.md#editor-support).

---

## Startup verification

`AddJsxCore()` verifies the environment **synchronously, at registration**, and throws
`JsxCoreEnvironmentException` if anything it needs is missing. This is on purpose: the alternative
is an app that starts cleanly and then 500s on the first view request, with far less context.

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
