# Import syntax

← [Documentation index](README.md)

Every import a view can write, and what each one resolves to.

Two kinds are ordinary JavaScript: your own files, and npm packages. The rest are JsxCore's, and
all use a scheme rather than a package name, because nothing behind them comes from npm:

| Written | Resolves to |
|---|---|
| `"./Card.tsx"`, `"@/Shared/Card.tsx"` | another view or component of yours |
| `"marked"`, `"preact/hooks"` | an npm package, or the framework |
| `"dotnet:types"`, `"dotnet:types/MyApp/Models"` | types generated from your .NET code |
| `"dotnet:globals"`, `"dotnet:rendering"` | the .NET objects you registered, and the view contract |
| `"dotnet:rendering/head"` | the `<Head>` component |
| `"/images/logo.svg"` | a static file of yours, as the URL it is served from |

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
file; an export is enough. Only a view, meaning a module returned from an endpoint, needs a default
export.

An import that resolves outside the views directory is refused.

### The `@/` alias

`@/` is the views directory, so a deep component can reach a shared one without counting `../`:

```tsx
import { Card } from "@/Shared/Card.tsx";
```

The same alias every Next.js and Vite template scaffolds, generated for you in both the compiler
configuration and the editor one. Aliases of your own work too: add them to
`options.CompilerOptions["paths"]` and they merge over the generated ones rather than replacing
them. Any alias landing inside the views directory is rewritten to a relative path when the view
compiles, which is what makes it resolve in a browser and not only in an editor.

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

`dotnet:` is the .NET side of the application: `types`, `globals` and `rendering`. These type
imports are erased during compilation and never reach the browser.

### By namespace

The usual form, and the one that reads like .NET:

```tsx
import type { Product } from "dotnet:types/MyApp/Models";
import type { Money } from "dotnet:types/MyApp/Models/Pricing";
```

The path after `dotnet:types` is the .NET namespace, with `/` for `.`. Nested namespaces nest.

**No assembly appears anywhere.** `MyApp.Models.Product` is `dotnet:types/MyApp/Models` whether it
was declared in the web project or in a contracts project that one references, which is how
`using MyApp.Models` behaves in C# too. Moving a type between projects does not move the specifier
that reaches it.

### The whole tree

```tsx
import type Types from "dotnet:types";

function show(product: Types.MyApp.Models.Product) { /* ... */ }
```

Everything is declared here, and the namespace modules above are facades aliasing it rather than
second declarations, so the two forms name the same type and are assignable to each other.

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

## Static assets

Static files live in `wwwroot`, served by `UseStaticFiles()`. Import one by the URL it is served
from:

```tsx
import logo from "/images/logo.svg";

export default function Header() {
    return <img src={logo} alt="Contoso" />;
}
```

The specifier and the URL are the same string, which is the point: `wwwroot/images/logo.svg` is
served at `/images/logo.svg`, so that is what you write. It resolves to that URL, typed `string`.

Nothing is copied or versioned. The point is the check: a typo becomes a compile error instead of a
broken image in production. `<img src="/images/logo.svg" />` by hand still works.

Any web asset extension resolves: images, fonts, video, `.pdf`.

**For everything but stylesheets, the path has to start at the root.** `./logo.svg` beside a view
names nothing JsxCore serves, because views are a source tree. It type checks anyway, since the
declarations behind this are `*.svg` wildcards and TypeScript rejects a pattern beginning with a
slash, so the build reports it by name instead. A rooted path naming a file the web root does not
hold is reported the same way.

Stylesheets are the exception, because JsxCore processes those. See below.

### Stylesheets

Three places a stylesheet can come from, and the spelling says which:

```tsx
import "/css/site.css";                     // your web root, served by UseStaticFiles
import "./card.css";                        // beside the component
import "some-widget/styles.css";            // an npm package's own styles
import styles from "./Card.module.css";     // a CSS module, with scoped class names
```

The first three bind nothing and are answered with a `<link>` in the document. A `*.module.css`
binds the scoped class names, so `styles.card` is what goes in the markup.

See [Styling](styling.md) for what each is for, how ordering is decided, and Tailwind.

---

## The view contract

```tsx
import { isServerRender } from "dotnet:rendering";
import type { ViewProps, HeadDescriptor } from "dotnet:rendering";
```

`isServerRender()` says which pass is running. `ViewProps<TModel>` is what every view is handed,
and `HeadDescriptor` is what a `head` export may return.

One sub-path, for the component that sets document head tags from inside the tree:

```tsx
import Head from "dotnet:rendering/head";

<Head><title>Products</title></Head>
```

See [Writing views](writing-views.md#the-document-head) for what it does and when to prefer the
`head` export.

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
| `import type ... from "dotnet:types"` | Yes | No |
| `import { Inventory } from "dotnet:globals"` | No | Yes |
| `import { isServerRender } from "dotnet:rendering"` | No | Yes |
| `import { marked } from "marked"` | No | Yes |
| `import { Card } from "./Card.tsx"` | No | No: a relative URL |
| `import logo from "/images/logo.svg"` | No | No: rewritten to a relative URL |
| `import Head from "dotnet:rendering/head"` | No | Yes |

The entries are generated for you. `options.ImportMap` adds your own, and wins over the generated
ones, which is how to point a package at a CDN. See
[Extensibility](extensibility.md#add-module-specifiers).

---

## See also

- [Writing views](writing-views.md): the view contract, `head`, hooks, the JSX dialect
- [Model types](model-types.md): what `dotnet:types` contains and how to control it
- [.NET interop](dotnet-interop.md): registering the objects behind `dotnet:globals`
- [npm packages](npm-packages.md): what resolves from `node_modules`, on both sides
