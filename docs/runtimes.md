# Runtimes

← [Documentation index](README.md)

Views compile and render against one of two JSX runtimes.

---

## Which one?

| | Built-in *(default)* | **Preact** *(recommended)* |
|---|---|---|
| Extra npm packages | none | `preact`, `preact-render-to-string` |
| Size on the wire | ~6.8 KB gzipped | ~7.8 KB gzipped |
| Hooks | useState/useEffect/useRef/useMemo/useCallback | the full set, including useReducer and useContext |
| Context | ✗ | ✓ |
| Error boundaries, portals, refs, memo | ✗ | ✓ |
| Hydration | replaces server markup | **true DOM-preserving hydration** |
| React ecosystem | ✗ | ✓ via `preact/compat` |

The built-in runtime exists so that `dotnet add package JsxCore` is all you need. It is a real
keyed reconciler with hooks, and it is fine for content pages and modest interactivity, but it is
not a full component model, and hitting one of its gaps is jarring if you are used to React.

Note that size is **not** a reason to prefer it. Gzipped, the two are within about a kilobyte of
each other, and Preact is the smaller of the two before compression because it ships minified. The
built-in runtime's only real advantage is that it needs no npm packages beyond the compiler.

**For anything substantial, use Preact.**

---

## Switching to Preact

```csharp
builder.AddJsxCore(options => options.UsePreact());
```

That is the whole switch. The packages are installed for you on the next build, provided
`<JsxCoreRuntime>preact</JsxCoreRuntime>` is set so the build knows which runtime to compile
against. It is not optional: a build that finds `UsePreact()` in your code without it fails with
`JSX0007` rather than producing output that only breaks once deployed. To add them yourself:

```bash
dotnet npm add preact preact-render-to-string   # or: npm install preact preact-render-to-string
```
 Views then compile against Preact's own JSX runtime and types:

```tsx
import { createContext } from "preact";
import { useContext, useReducer, useState } from "preact/hooks";
import type { ComponentChildren } from "preact";

const Currency = createContext("£");

export function Price({ amount }: { amount: number }) {
    return <strong>{useContext(Currency)}{amount.toFixed(2)}</strong>;
}
```

Startup fails with an explanatory message if the packages are not installed.

### Still no bundler

Preact publishes real ES modules. JsxCore copies the `.mjs` files from your `node_modules` into
`obj/JsxCore/preact/` **verbatim** and serves them. There is nothing to bundle, the version you
install is the version that runs, and upgrading is a version bump in `package.json`.

The copy is cached against the installed version, so it happens once rather than on every build.

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

## Why not React itself?

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
`preact/compat` keeps the ecosystem. That trade looked one-sided enough to make it the
recommendation.

---

## The built-in runtime

If you stay on the default, this is what you get:

```tsx
import { useState, useEffect, useRef, useMemo, useCallback } from "@jsxcore/runtime";
import type { ViewProps, JsxNode } from "@jsxcore/runtime";
```

A keyed reconciler with component-local state, event handlers, refs and effects. Enough for forms,
filters, counters and toggles. What it does **not** have: context, suspense, portals, error
boundaries, concurrent rendering, and on mount it replaces server-rendered markup rather than
hydrating it in place.

Everything else in this documentation works identically in both runtimes: render modes, `head`
exports, .NET interop, generated model types, hot reload.
