# Runtimes

← [Documentation index](README.md)

Views compile and render against **Preact**, which ships inside JsxCore, or against **React**,
which the build restores for you. One property in the project file decides.

---

## Which framework

| | **Preact** *(default)* | **React** |
|---|---|---|
| Where it comes from | inside the JsxCore package | npm, restored by the build |
| To install | nothing | nothing: the build does it |
| In the publish output | nothing | `react`, `react-dom` |
| Type declarations | shipped with it | `@types/react`, restored by the build |
| React ecosystem | most of it, via `preact/compat` | all of it |
| Server render, per view | ~0.25 ms | ~0.53 ms |

**Preact unless something needs the real React.** It is the default, it needs nothing installed, and
`preact/compat` covers most of the ecosystem. Choose React when a dependency will not tolerate the
substitute, or when you want React's exact semantics.

---

## Switching frameworks

One property, in the project file:

```xml
<PropertyGroup>
  <JsxCoreFramework>react</JsxCoreFramework>
</PropertyGroup>
```

`preact` is the default and can be omitted. Naming `react` makes the build restore `react`,
`react-dom`, `@types/react` and `@types/react-dom`, compile views against React's JSX runtime, and
serve React to the browser. Nothing else changes: render modes, `head` exports, .NET interop,
generated model types and hot reload all behave identically.

What does change is anything of yours that names a framework. Framework packages are imported by
their own names, so `preact/hooks` has to become `react`, and a view written against Preact's own
API rather than through [React compatibility](#react-compatibility) needs the same treatment.

JsxCore's own client entry point has a name that survives the switch:

```ts
import { mountView } from "@jsxcore/client";
```

That resolves to the same file as `@jsxcore/preact/client` or `@jsxcore/react/client`, whichever is
in play, so code that mounts a view keeps working across the change. It is what the document JsxCore
generates imports, and what a
[custom document template](extensibility.md#replace-the-whole-document) is handed as
`DocumentContext.ClientSpecifier`, so most applications get it without naming it.

It lives in the project file rather than in `AddJsxCore` because the build acts on it: it decides
which packages are restored and which JSX runtime views compile against, all of which happens before
a line of your code runs. A setting the build has to obey belongs where the build can see it. The
build records the choice on the application's assembly, so the application knows at startup which
runtime to serve without being told twice.

Naming a framework JsxCore does not know fails the build rather than guessing:

```
error JSX0007: JsxCore does not know the framework 'vue'.
<JsxCoreFramework> takes 'preact' or 'react'.
```

### What React costs

React publishes CommonJS and no type declarations, so both are worked around rather than avoided:
its modules are wrapped for the browser by the same interop that serves any other CommonJS package,
and its types come from DefinitelyTyped. Both sides also need globals their environment does not
have (`MessageChannel` and `TextEncoder` in the server engine, `process.env.NODE_ENV` in a browser),
which JsxCore supplies.

None of that needs configuring. It is why React is the option rather than the default: it is more
moving parts, a slower render, and two more packages in the publish output, in exchange for being
the real thing.

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

## Why Preact is the default

React is still published as CommonJS, so it cannot be served to a browser without being converted
into ES modules first, and `react-dom/server` references `MessageChannel` and `TextEncoder` at
module scope, neither of which an embedded engine provides, so it needs host shims before it will
even evaluate.

Both are surmountable. The result is a heavier dependency that renders roughly **half as fast** on
the server:

| | React 19 | Preact 10 |
|---|---|---|
| Module format | CommonJS → wrapped by JsxCore | real ESM |
| Runs in an embedded engine | only with host shims | unmodified |
| JavaScript the engine must parse | 536 KB | 15 KB |
| First module load | 126 ms | 76 ms |
| **Per render** | 0.53 ms | **0.248 ms** |

Preact gives the same programming model, keeps the no-bundler design intact, and via
`preact/compat` keeps the ecosystem. That trade is one-sided enough to make it what JsxCore ships
and what you get by default. React remains a property away when you need it, with both problems
above handled for you rather than avoided.

---

## Everything else is the same

Render modes, `head` exports, .NET interop, generated model types and hot reload are all unaffected
by any of this: they sit above the framework and behave identically whatever renders the view.
