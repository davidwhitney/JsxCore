# Roadmap

← [Documentation index](README.md)

What is not built yet, in the order I would build it, with enough design recorded that starting is
execution rather than rediscovery. Nothing here is scheduled.

---

## 1. Delete the built-in renderer

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

## 2. A component testing story

Vitest or Jest against a `.tsx`. JsxCore cannot provide this without Node, so the honest answer may
be "render through `WebApplicationFactory` and assert on the markup", which [Testing](testing.md)
already covers, but that is not said anywhere a frontend developer will look. Deciding the
recommendation and writing it down is most of this item.

---

## 3. Environment variables in views

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
- **Images beside a component.** An image in the views directory is not served and cannot be
  imported. Views are a source tree; `wwwroot` is what the application serves. Stylesheets are the
  exception, because those are processed rather than served as they are found.
- **npm `.bin` linking.** The native client does not create `node_modules/.bin`, because linking
  executables is a lifecycle-script job it deliberately does not run. Any npm tool with a CLI must
  be invoked by path, as [Styling](styling.md#tailwind-needs-node) documents.
- **Top-level `await` in a view.** It works: an already-resolved promise awaits and the view
  renders. Anything needing a timer fails with `setTimeout is not defined`, because the engine has
  no event loop, which is a clear enough failure to leave alone.
- **Client-side routing.** React Router works but competes with ASP.NET routing. Which one owns the
  URL is a question for whoever is building the application.

WebAssembly interop, compiling the exported .NET surface so client-rendered views could call it
directly, is not on this list. The design notes are in git history if it ever comes back.

---

## Order

Item 1 is deletion, the cheapest of these and the only one that makes the library smaller. Items 2
and 3 are writing and choosing a shape.
