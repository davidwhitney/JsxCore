# How it works

← [Documentation index](README.md)

---

## Why this exists

Razor is excellent at server rendering and poor at interactivity. A separate SPA is excellent at
interactivity and drags in a build pipeline, a second router, and a serialisation boundary you now
maintain by hand.

JsxCore takes the middle path. Views are components. The same component can render on the server
for first paint and SEO, or in the browser for interactivity, or both, chosen per response. The
model flows from your endpoint into the component either way, and its TypeScript type is generated
from the C#, so the two cannot drift.

---

## The pipeline

```
Views/Home/Index.tsx
        │
        │  tsc (native Go binary)   tens of milliseconds for a whole view tree
        ▼
obj/JsxCore/js/Home/Index.js        ES module, imports rewritten to .js
        │
        ├─────────────────────────────────┐
        ▼                                 ▼
  Browser (client render)          Jint (server render)
  fetches /_jsx/v{build}/...         loads the same files in-process
  resolves imports natively        .NET globals injected as CLR objects
```

Both sides load **the same compiled files**. There is no separate server bundle and no separate
client bundle, so a component cannot behave differently depending on which one ran.

---

## No bundler

The three things that usually make this kind of tool heavy are all absent.

### Compilation is a native binary

TypeScript 7 is the Go port of the compiler, published as platform-specific native executables
under `node_modules/@typescript/typescript-{os}-{arch}/lib/tsc`. JsxCore locates that binary and
runs it as a process.

npm downloads that package, and npm runs on Node, so Node has to exist on a development or build
machine. **Nothing runs on Node afterwards**: not the compilation, not the rendering, not a request.
There is no Node process to supervise, and a production server needs neither npm nor Node.

A whole view tree compiles in tens of milliseconds, which is what makes compiling on startup and
on every file change practical.

### Module loading is the browser's

The generated `tsconfig.json` sets `rewriteRelativeImportExtensions`, so this:

```tsx
import { Card } from "../Shared/Card.tsx";
```

emits as this:

```js
import { Card } from "../Shared/Card.js";
```

That is a specifier the browser can fetch directly. The browser walks the import graph itself, so
there is nothing to bundle. Bare specifiers (`@jsxcore/runtime`, `preact`, your own additions)
are wired up with a generated [import map](extensibility.md#add-module-specifiers).

### Server rendering is in-process

Compiled modules are executed by [Jint][jint], a JavaScript engine written in .NET, inside your
application process. Because there is no process boundary, .NET objects exposed to a view are
*real objects*: calls are synchronous and direct, not RPC over a bridge. That is what makes
[.NET interop](dotnet-interop.md) feel like calling a service rather than fetching from one.

[jint]: https://github.com/sebastienros/jint

---

## Build ids and caching

Compiled modules are served from:

```
/_jsx/v{buildId}/views/Home/Index.js
/_jsx/v{buildId}/runtime/client.js
/_jsx/v{buildId}/preact/preact.js
```

The build id is a content hash, and it lives in the **path** rather than a query string. That does
three useful things:

1. **Assets are immutable.** They are served with a one-year `Cache-Control`, safely, because a
   change produces a different URL.
2. **A rebuild swaps the whole graph.** A relative import inside a module resolves against the
   same versioned prefix, so re-importing one view pulls fresh copies of everything it depends on
   without cache-busting a single import statement.
3. **Hot reload is trivial.** The reload client just re-imports the view from the new prefix.

Identical sources produce an identical build id across restarts, so caches survive a redeploy that
did not change anything. The build of JsxCore is folded in alongside them, because the same view and
the same package can compile to different output under a different version.

---

## The embedded runtime

`@jsxcore/runtime` (the JSX factory, the client renderer, the server renderer, the hooks and the
`.NET` bridge) is compiled into the JsxCore assembly as embedded resources.

It is served to the browser and loaded by the server renderer **straight from the assembly
manifest**. It never appears as files in your project, cannot be edited by accident, and cannot
drift from the library version.

The only files written to disk are the TypeScript declarations, because the compiler is a separate
process that has to read them, and those go into `obj/JsxCore/runtime/`.

In [Preact mode](runtimes.md), Preact's own `.mjs` files are copied verbatim out of your
`node_modules` into `obj/JsxCore/preact/` and served from there. The version you install is
the version that runs.

---

## What happens on a request

**Client mode.** The server emits an HTML shell containing the serialised model, the import map
and a module script that mounts the component. The component itself never runs on the server,
though its `head` export is still evaluated so the document gets a title.

**Server mode.** The component runs in Jint and its markup is written into the response. No
JavaScript is sent at all.

**ServerAndClient.** Both. The markup is produced on the server for first paint, and the same
component is then mounted in the browser, hydrating the existing DOM in Preact mode.

See [Render modes](render-modes.md) for the details.
