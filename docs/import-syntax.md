# Import syntax

← [Documentation index](README.md)

Every import a view can write, and what each one resolves to.

There are four kinds. Two are ordinary JavaScript — your own files, and npm packages. Two are
JsxCore's, and both use a scheme rather than a package name, because nothing behind them comes
from npm:

| Written | Resolves to |
|---|---|
| `"./Card.tsx"` | another view or component of yours |
| `"marked"`, `"preact/hooks"` | an npm package, or the framework |
| `"dotnet:MyApp"`, `"dotnet:MyApp/Models"` | types generated from your .NET assembly |
| `"dotnet:globals"`, `"dotnet:rendering"` | the .NET objects you registered, and the view contract |

---

## Your own components

Plain ESM, with the **real extension**:

```tsx
import { Card, Nav } from "../Shared/Layout.tsx";
import { formatMoney } from "./money.ts";
```

TypeScript rewrites the extension on the way out, so `./Card.tsx` is emitted as `./Card.js` and the
browser fetches that. Writing `./Card` without an extension does not work: the compiler leaves it
alone, and the browser will not probe for a file that resolves it.

Components can live anywhere under the views directory. There is no registration step and no barrel
file; an export is enough. Only a view — a module returned from an endpoint — needs a default
export.

An import that resolves outside the views directory is refused.

---

## npm packages

Bare specifiers, exactly as anywhere else:

```tsx
import { marked } from "marked";
import { nanoid } from "nanoid/non-secure";
```

JsxCore follows the graph from your compiled views, resolves every bare specifier, and generates an
[import map](extensibility.md#add-module-specifiers) so the browser can do the same. Nothing is
bundled and there is no configuration to write. See [npm packages](npm-packages.md) for what
resolves, and for the CommonJS story: a package published as CommonJS is wrapped automatically, but
a **default import** is the reliable form.

```tsx
import pkg from "some-commonjs-package";
const { thing } = pkg;
```

`devDependencies` are deliberately not served to the browser, because they are not in the publish
output. Importing one from a client-rendered view logs a warning naming the package rather than
failing later in production.

### The framework

The framework is imported like any other package:

```tsx
import { useState } from "preact/hooks";     // Preact, the default
import { useState } from "react";            // React mode
```

In Preact mode `react` and `react-dom` are mapped onto `preact/compat`, so components written
against React resolve unchanged. See [Runtimes](runtimes.md).

---

## Types from .NET

`dotnet:` is the .NET side of your application: an assembly by name, or one of the two names JsxCore
reserves. These imports are **types only**, so they are erased during compilation and never reach
the browser.

### By assembly

```tsx
import type MyApp from "dotnet:MyApp";

function show(product: MyApp.Models.Product) { /* ... */ }
```

The default import binds the assembly's **root namespace**, and types are reached through their
full .NET namespace below it. If the root namespace and the assembly name differ — assembly
`MyApp.Web`, types in `Contoso.Models` — the qualifier is the namespace, not the assembly:
`import type Contoso from "dotnet:MyApp.Web"`, then `Contoso.Models.Product`.

### By namespace

Usually the one you want, and the one that reads like .NET:

```tsx
import type { Product } from "dotnet:MyApp/Models";
import type { Money } from "dotnet:MyApp/Models/Pricing";
```

The path after the assembly is the namespace, with `/` for `.`. A namespace that repeats the
assembly name sheds it, so `MyApp.Models` is `dotnet:MyApp/Models` rather than
`dotnet:MyApp/MyApp/Models`. Nested namespaces nest.

Both forms name the same type — the namespace modules alias the assembly module rather than
declaring anything twice — so they are interchangeable and assignable to each other.

See [Model types](model-types.md) for what gets exported and how to control it.

---

## Objects from .NET

Each object registered with `options.Globals` is a named export, typed from the C# it was
registered as:

```tsx
import { Inventory } from "dotnet:globals";

const total = Inventory.getTotalValue();     // checked against your C# method
```

Unlike the type imports above, this one **is** sent to the browser, so that a view rendering on both
sides can import it. Using a global on the client throws with an explanatory message; guard it with
`isServerRender()` if a view runs in both modes.

`dotnet` is exported from the same module and reaches any global by name. It is untyped, and is
what to use for a global registered under a name that is not a valid identifier:

```tsx
import { dotnet } from "dotnet:globals";

const service = dotnet["my service"];
```

Until the application has run once the build cannot know what is registered, so these imports type
as `any` and sharpen after a run. See [.NET interop](dotnet-interop.md).

---

## The view contract

```tsx
import { isServerRender } from "dotnet:rendering";
import type { ViewProps, HeadDescriptor } from "dotnet:rendering";
```

`isServerRender()` says which pass is running. `ViewProps<TModel>` is what every view is handed,
and `HeadDescriptor` is what a `head` export may return. See
[Writing views](writing-views.md).

---

## Dynamic imports

Ordinary `import()` works, which is how to keep a browser-only package out of the server pass:

```tsx
import { isServerRender } from "dotnet:rendering";

if (!isServerRender()) {
    const { default: chart } = await import("chart.js");
}
```

---

## What reaches the browser

| Import | Erased at compile time | Needs an import map entry |
|---|---|---|
| `import type ... from "dotnet:MyApp"` | Yes | No |
| `import { Inventory } from "dotnet:globals"` | No | Yes |
| `import { isServerRender } from "dotnet:rendering"` | No | Yes |
| `import { marked } from "marked"` | No | Yes |
| `import { Card } from "./Card.tsx"` | No | No — a relative URL |

The entries are generated for you. `options.ImportMap` adds your own, and wins over the generated
ones, which is how to point a package at a CDN. See
[Extensibility](extensibility.md#add-module-specifiers).

---

## See also

- [Writing views](writing-views.md) — the view contract, `head`, hooks, the JSX dialect
- [Model types](model-types.md) — what `dotnet:<Assembly>` contains and how to control it
- [.NET interop](dotnet-interop.md) — registering the objects behind `dotnet:globals`
- [npm packages](npm-packages.md) — what resolves from `node_modules`, on both sides
