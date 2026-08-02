# Roadmap

← [Documentation index](README.md)

What is not built yet, in the order I would build it, with enough design recorded that starting is
execution rather than rediscovery. Nothing here is scheduled.

---

## 1. CSS Modules, and CSS processing generally

```tsx
import styles from "./Card.module.css";

<div class={styles.card} />
```

Needs a name-mangling contract: the compiler rewrites class names, the generated module exports the
map, the emitted stylesheet agrees with both. Tailwind sidesteps this, which is part of why it was
the right thing to prove first.

Two constraints from the earlier design work still hold:

- **When it runs.** Views compile in tens of milliseconds; a Node-based CSS pipeline does not.
  In the synchronous startup path it would undo the fast feedback loop, so it belongs on the
  watcher's schedule.
- **Scoping reaches into the JSX transform**, which is what makes this bigger than copying files.

The third, ordering, is settled: asset imports record the compiled module graph, and a page's
stylesheets come from a walk of it: dependencies first, each once, independent of render order.
CSS processing inherits that.

Open: **where a processed stylesheet lives, and where it goes.** An imported stylesheet today is a
file in `wwwroot`, named by the URL it is served from, which is why JsxCore neither copies nor
versions it. A `.module.css` is neither: a source file, so it belongs beside the component and would
be spelled relatively, and a build output, so it needs a URL carrying a build id. Two kinds of
stylesheet in one feature, and the design should say which is which.

The same pipeline answers Sass and PostCSS: the Tailwind pattern with a different binary.

**How we would know it works:** a view importing a stylesheet renders a link to it and one that does
not does not; changing a stylesheet moves the build id and therefore the URL, as changing a view
does; publish output contains the processed stylesheet and no source, and serves it with no
processor installed.

---

---

## 2. Type modules per assembly

Every generated type lands in one module named after the application assembly, whatever assembly
actually declared it. A model from a referenced project ends up at a specifier naming the wrong
assembly, with its own namespace hanging off it:

```
dotnet:MyApp.Web/MyApp/Contracts/Models     what you get
dotnet:MyApp.Contracts/Models               what you would reach for, which does not exist
```

A solution with a separate contracts or domain project is ordinary, and that is exactly where its
models live, so this is met more often than its size suggests.

**The work.** One module per declaring assembly, with namespace sub-modules under each, as there
are today under the single one.

The hard part is what the present design is built to avoid. Everything is declared in one file
precisely so that references between namespaces resolve without imports, and the namespace modules
are facades aliasing it. Splitting by assembly means a model in one assembly referencing a type in
another needs a real generated `import type`.

One property makes that tractable: .NET forbids circular project references, so the import graph
between generated modules is acyclic by construction. Nothing has to tolerate a cycle, which is the
usual reason this kind of split turns nasty.

**Decide before writing code.** Whether an assembly declaring no exported types is emitted at all,
and what a view imports when a type is reachable through two assemblies because one re-exports the
other.

**How we would know it works:** a model in a referenced assembly is importable from a specifier
naming that assembly; a model referencing a type across an assembly boundary still type checks; and
the application's own module no longer contains anything it did not declare.

---

## 3. Delete the built-in renderer

`runtime/client.js`, `server.js`, `dom.js`, `hooks.js`, `jsx-runtime.js` and `jsx-dev-runtime.js`
are a closed island left from before Preact was vendored. About 1,400 lines including declarations,
and nothing in the live path reaches them: they import each other and nothing else imports them.

It is not only dead weight. `index.js` re-exports the island's hooks, so importing `useState` from
`dotnet:rendering` compiles, resolves, and then never runs, which is the worst shape a trap can
take.

**What has to survive.** `index.js` and `index.d.ts` stay: the module loader resolves
`dotnet:rendering` to the first, and the second is where `ViewProps`, `HeadDescriptor` and
`isServerRender` are declared. They stop re-exporting `jsx-runtime.js` and `hooks.js`, which is the
part that closes the trap. `dotnet.js`, `head.js`, `host-shims.js` and `hmr-client.js` are all live
and unaffected.

**How we would know it worked:** the suite passes untouched, since nothing tests the island
directly, and a view importing `useState` from `dotnet:rendering` stops compiling instead of
failing at run time.

---

## 4. A component testing story

Vitest or Jest against a `.tsx`. JsxCore cannot provide this without Node, so the honest answer may
be "render through `WebApplicationFactory` and assert on the markup", which [Testing](testing.md)
already covers, but that is not said anywhere a frontend developer will look. Deciding the
recommendation and writing it down is most of this item.

---

## 5. Environment variables in views

`process.env.API_URL`, or `import.meta.env.MODE`. Today `process` exists only inside the CommonJS
wrapper, so a view referencing it breaks in the browser. That is the exact bug class that broke
React before the wrapper supplied one.

The shape that seems right is an explicit allow-list injected into the import map, rather than
ambient access to the server's environment. Leaking configuration into the browser by accident is
the risk worth designing against.

---

## Decided against

Recorded so they are not rediscovered as gaps:

- **Vite's `?url` and `?raw` suffixes.** `?raw` reads a file's contents into a module, which is a
  different feature from naming a URL.
- **Content hashing for imported assets.** They are served by `UseStaticFiles` at their own URL,
  cached however that middleware is configured rather than immutably like a compiled view. ASP.NET
  Core's static web assets already fingerprint.
- **Assets beside a component.** An image in the views directory is not served and cannot be
  imported. Views are a source tree; `wwwroot` is what the application serves.
- **npm `.bin` linking.** The native client does not create `node_modules/.bin`, because linking
  executables is a lifecycle-script job it deliberately does not run. Any npm tool with a CLI must
  be invoked by path, as [Tailwind](tailwind.md) documents.
- **Top-level `await` in a view.** It works: an already-resolved promise awaits and the view
  renders. Anything needing a timer fails with `setTimeout is not defined`, because the engine has
  no event loop, which is a clear enough failure to leave alone.
- **Client-side routing.** React Router works but competes with ASP.NET routing. Which one owns the
  URL is a question for whoever is building the application.

WebAssembly interop, compiling the exported .NET surface so client-rendered views could call it
directly, is not on this list. The design notes are in git history if it ever comes back.

---

## Order

Item 1 is the biggest remaining gap between this and what someone arriving from Next.js expects,
and its ordering half is answered. Item 2 is the one an ordinary solution layout runs into. Item 3
is deletion, the cheapest of these and the only one that makes the library smaller. Items 4 and 5
are writing and choosing a shape.
