# Runtimes

← [Documentation index](README.md)

Views compile and render against **Preact**, which ships inside JsxCore.

---

## Nothing to choose

There is one framework and it is already there. `dotnet add package JsxCore` gives you a full
component model: context, error boundaries, refs, portals, memo, the complete hook set, true
DOM-preserving hydration, and most of the React ecosystem through `preact/compat`.

Nothing is installed, nothing is added to `package.json`, and nothing extra appears in the publish
output. There is no setting to get wrong.

JsxCore used to carry a small runtime of its own so that the package alone was enough to render
something. Vendoring Preact made that redundant: the same "install nothing" promise now comes with
a real component model rather than a reduced one, so the built-in runtime is gone.

---

## Switching frameworks

One property, in the project file:

```xml
<PropertyGroup>
  <JsxCoreFramework>preact</JsxCoreFramework>
</PropertyGroup>
```

`preact` is the default and can be omitted; it is the only value implemented today.

It lives in the project file rather than in `AddJsxCore` because the build acts on it: it decides
which packages are restored and which JSX runtime views are compiled against, all of which happens
before a line of your code runs. A setting the build has to obey belongs where the build can see it.

Asking for a framework that is not implemented fails the build rather than quietly rendering with
another one:

```
error JSX0007: JsxCore cannot compile against React yet.
<JsxCoreFramework>react</JsxCoreFramework> is recognised but not implemented.
```

React itself is the intended second value. It is not wired up: React publishes no ES modules and no
type declarations of its own, so it needs both to go through the CommonJS interop and two more
packages installed, which is a piece of work rather than a flag.

---

## Writing views

```tsx
import { createContext } from "preact";
import { useContext, useReducer, useState } from "preact/hooks";
import type { ComponentChildren } from "preact";

const Currency = createContext("£");

export function Price({ amount }: { amount: number }) {
    return <strong>{useContext(Currency)}{amount.toFixed(2)}</strong>;
}
```

### Where Preact comes from

**It ships inside JsxCore.** Preact and preact-render-to-string are copied verbatim from npm into
the package, along with their type declarations, so a project renders with Preact having installed
nothing, published nothing extra, and reached no registry. Both are MIT licensed and their licences
travel with them.

Nothing is modified. The files JsxCore carries are the ones npm publishes, staged into
`obj/JsxCore/preact/` and served as real ES modules. There is still no bundler and no build step of
their own.

### Upgrading Preact

Install the version you want and JsxCore uses it instead:

```bash
dotnet npm add preact --version ^10          # or: npm install preact@^10
```

An installed copy always wins over the one shipped in the package, so you are never waiting for a
JsxCore release to move to a newer Preact. The choice is made per module: install `preact` alone and
your Preact is used while `preact-render-to-string` still comes from JsxCore, which is fine, as the
two are versioned separately anyway.

Type declarations follow the same rule, so views type check against the version they will actually
run.

The versions carried in the package are recorded in
[`src/JsxCore/Assets/vendor/preact/README.md`](../src/JsxCore/Assets/vendor/preact/README.md), and
the installed version, when there is one, is part of the asset build id: upgrading changes every
Preact URL, so browser caches do not serve the old one.

To go back to the shipped copy, uninstall the package again.

---

## React compatibility

`preact/compat` is mapped onto the React specifiers by default, so components and libraries written
against React resolve unchanged, in the browser and in the server renderer alike:

```tsx
import { useState, memo, forwardRef } from "react";   // resolves to preact/compat
```

Type checking follows the same mapping, so your editor agrees with the runtime. Turn it off if you
would rather the React specifiers failed to resolve:

```csharp
options.EnableReactCompatibility = false;
```

---

## Why Preact and not React

React is still published as CommonJS, so it cannot be served as ES modules without a bundling step,
and `react-dom/server` references `MessageChannel` and `TextEncoder` at module scope, neither of
which an embedded engine provides, so it needs host shims before it will even evaluate.

Both are surmountable. The result is a heavier dependency that renders roughly **half as fast** on
the server:

| | React 19 | Preact 10 |
|---|---|---|
| Module format | CommonJS → needs a bundler | real ESM |
| Runs in an embedded engine | only with host shims | unmodified |
| JavaScript the engine must parse | 536 KB | 15 KB |
| First module load | 126 ms | 76 ms |
| **Per render** | 0.53 ms | **0.248 ms** |

Preact gives the same programming model, keeps the no-bundler design intact, and via
`preact/compat` keeps the ecosystem. That trade is one-sided enough to make it the framework JsxCore
ships, and it is why `<JsxCoreFramework>react</JsxCoreFramework>` is a recognised value rather than
a working one: supporting React properly means solving both problems above, not setting a flag.

---

## Everything else is the same

Render modes, `head` exports, .NET interop, generated model types and hot reload are all unaffected
by any of this: they sit above the framework and behave identically whatever renders the view.
