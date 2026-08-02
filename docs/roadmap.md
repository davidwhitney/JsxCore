# Roadmap

← [Documentation index](README.md)

---

What is not built yet, in the order I would build it, with enough design recorded that starting is
execution rather than rediscovery.

Nothing here is scheduled. Two long-standing items — WebAssembly interop and CSS processing — had
fuller design notes in earlier revisions of this file, including their hard parts and how we would
know they worked. Those are in git history if the summaries below are not enough.

---

## Shipped since this list was written

So the rest is not read against a stale picture:

- **npm packages restore without npm.** The native client resolves, fetches, verifies and unpacks,
  and writes a `package-lock.json` that real `npm ci` accepts. `dotnet npm` puts the same client on
  the command line. This closed what used to be item 1.
- **React alongside Preact**, chosen with `<JsxCoreFramework>`.
- **Minification and compression**, on by default for Release: views by the build, packages at
  publish, Brotli or gzip at serve time.
- **Precompiled by default for Release**, so a published application serves what the build produced
  rather than compiling on a server.
- **The `dotnet:` scheme** — assembly types, namespace sub-paths, registered globals, and the
  rendering contract.
- **Tailwind**, which works today with about fifteen lines of setup. See [Tailwind](tailwind.md).
- **CSP nonces** on every script JsxCore writes.

---

## 1. Static asset imports

```tsx
import logo from "./logo.svg";
import "./styles.css";
```

Muscle memory from Vite and webpack, and the most likely "why doesn't this work". Today both fail:
views can only import JavaScript, and an asset sitting in `Views/` is never served.

**The hard part** is that `./logo.svg` is not JavaScript. TypeScript leaves the specifier alone, so
the browser fetches `/_jsx/v…/views/logo.svg` expecting a module and gets an image. Vite rewrites
the specifier at build time, and so must this.

### The work, in order

1. **Stage the assets.** Copy non-`.ts`/`.tsx` files from the views directory into the compiled
   output, and teach `ContentTypeFor` the extensions — `.svg`, `.png`, `.woff2` and friends. `.css`
   and `.map` are already handled.
2. **Generate a shim module per asset**: `logo.svg.js` containing
   `export default "/_jsx/v…/views/logo.svg"`.
3. **Rewrite the emitted imports** after compilation, turning `from "./logo.svg"` into
   `from "./logo.svg.js"`. This is the seam minification already occupies, beside `MinifyDirectory`.
4. **Generate ambient declarations** so the import type-checks: an `assets.d.ts` declaring `*.svg`,
   `*.png` and so on as `string`, included the way `pending.d.ts` already is.
5. **Carry them through publish**, the same shape as the npm dependency closure.

### Decide before writing code

- **Is `import "./styles.css"` in the first pass?** I would say yes: it is half the reason people
  want this, and it feeds directly into CSS Modules below.
- **Vite's `?url` and `?raw` suffixes?** Leave them out until someone asks.

### Worth doing regardless

Assets in `wwwroot`, referenced by path with `UseStaticFiles()`, already work. That is not this
item, but it is what people can do today and it is written down nowhere. A short section in
[Writing views](writing-views.md) would stop the question being asked at all.

---

## 2. CSS Modules, and CSS processing generally

```tsx
import styles from "./Card.module.css";

<div class={styles.card} />
```

Needs item 1 first, plus a name-mangling contract: the compiler rewrites class names, the generated
shim exports the map, and the emitted stylesheet has to agree with both. Tailwind sidesteps this
entirely, which is part of why it was the right thing to prove first.

Three constraints from the earlier design work still hold:

- **Ordering.** CSS is order dependent in a way ES modules are not. Two views importing the same
  stylesheet in different orders must not produce different results, so emission order has to come
  from the graph rather than from render order.
- **When it runs.** Views compile in tens of milliseconds. A Node-based CSS pipeline is slower, and
  putting it in the synchronous startup path would undo the fast feedback loop; it belongs on the
  watcher's schedule.
- **Scoping reaches into the JSX transform**, which is what makes this bigger than copying files.

The same pipeline answers Sass and PostCSS, which are otherwise the Tailwind pattern with a
different binary: an external CLI producing a stylesheet.

**How we would know it works:** a view importing a stylesheet renders a link to it and a view that
does not does not; changing a stylesheet moves the build id and therefore the URL, exactly as
changing a view does; publish output contains the processed stylesheet and no source, and the
application serves it with no processor installed.

---

## 3. Environment variables in views

`process.env.API_URL`, or `import.meta.env.MODE`. Today `process` exists only inside the CommonJS
wrapper, so a view referencing it breaks in the browser — the exact bug class that broke React
before the wrapper supplied one.

The shape that seems right is an explicit allow-list injected into the import map, rather than
ambient access to the server's environment. Leaking configuration into the browser by accident is
the risk worth designing against.

---

## 4. A component testing story

Vitest or Jest against a `.tsx`. JsxCore cannot provide this without Node, so the honest answer may
be "render through `WebApplicationFactory` and assert on the markup", which [Testing](testing.md)
already covers — but that is not said anywhere a frontend developer will look. Deciding the
recommendation and writing it down is most of this item.

---

## 5. Loose ends worth closing

Small, known, and each found while doing something else:

- **Verify tsconfig path aliases.** User `CompilerOptions` merge into both generated configs, so
  `paths` such as `@/components/*` should survive — but the `dotnet:*` mapping merges into the same
  object and the interaction is untested.
- **Per-assembly type modules.** `dotnet:<Assembly>` is named per assembly but contains every
  declared type, including any pulled in from referenced assemblies. Splitting them needs generated
  cross-module imports for types that reference each other.
- **Delete the built-in renderer.** `runtime/client.js`, `server.js`, `dom.js`, `hooks.js` and the
  JSX runtime files — about 1,400 lines including declarations — are a closed island nothing in the
  live path imports, left from before Preact was vendored. `index.js` still re-exports its hooks,
  which is a quiet trap: importing `useState` from the runtime compiles and never runs.
- **npm `.bin` linking.** The native client does not create `node_modules/.bin`, because linking
  executables is a lifecycle-script job it deliberately does not run. Any npm tool with a CLI must
  be invoked by path, as [Tailwind](tailwind.md) documents.
- **Top-level `await` in a view.** Legal ESM that Jint may not survive, and server rendering is
  synchronous by design. Find out what it does today and make the failure explicit, as async
  components already are.
- **Client-side routing.** React Router works but competes with ASP.NET routing. A documentation and
  opinion question rather than a feature: say which one owns the URL.

---

## 6. Call .NET from the browser, not just from the server

Server-rendered views call .NET objects directly, in process:

```tsx
export default function Dashboard() {
    return <p>{Inventory.getSummary().total}</p>;
}
```

Client-rendered views cannot, and the documentation tells you to guard the call with
`isServerRender()`. That asymmetry is the sharpest edge in the model: the same view, the same call,
working or not depending on where it ran.

The proposal is to compile the exported .NET surface to WebAssembly, load it in the browser, and
generate TypeScript shims so the call means the same thing on both sides. It is by far the largest
item here, and it changes the programming model rather than filling a gap.

---

## Order

Items 1 and 2 are one piece of work in two stages, and together they close the biggest gap between
this and what someone arriving from Next.js expects. Item 3 is small and mostly about choosing a
safe shape. Item 4 is writing rather than building.

Item 6 is worth more than all of them, and should not start until the synchronous-or-not question
has an answer that survives contact with a real application.
