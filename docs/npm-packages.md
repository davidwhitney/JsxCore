# Using npm packages

← [Documentation index](README.md)

Views can import packages from `node_modules`. Install one and it works in both render modes:

```bash
dotnet npm add marked
```

```tsx
import { marked } from "marked";

export default function Article({ model }: { model: { body: string } }) {
    return <div dangerouslySetInnerHTML={{ __html: marked.parse(model.body) as string }} />;
}
```

Server rendering resolves the import out of `node_modules` directly. Client rendering gets an import
map entry pointing at the package, which JsxCore serves from the app. There is no bundler step and
nothing to configure.

---

## dependencies and devDependencies

`package.json` is the record. JsxCore reads it during the build and treats the two blocks the way
npm does:

| Block | Server rendering | Sent to the browser | In the publish output |
|---|---|---|---|
| `dependencies` | Yes | Yes | Yes |
| `devDependencies` | Yes | No | No |

So `dotnet npm add marked` makes a package available everywhere, and `dotnet npm add marked --dev`
keeps it to the build. The TypeScript compiler lives in `devDependencies` for exactly that reason.

A devDependency is deliberately not sent to the browser: it is not copied into the publish output,
so a client-rendered view importing one would work in development and fail in production. Rather
than let that surface as an unresolved specifier in the browser, JsxCore leaves it out of the import
map and logs a warning naming the package.

Nothing to declare in the project file. The manifest and lock file stay the real record, so
everything that reads them keeps working, from `npm audit` and `npm ci` to Dependabot and Renovate.

### Installing them

`dotnet build` restores whatever `package.json` declares and is not installed, and
`dotnet npm add marked` adds one without a build. Neither needs npm or Node on the machine.

If you have npm, keep using it: `npm install marked` works exactly as it always did, and the build
reads what it installed without touching the lock file it wrote.

See **[Package management](package-management.md)** for the command line reference, how the lock
file is written, how close resolution is to npm's, and
[what other package managers do](package-management.md#using-npm-or-another-package-manager).

---

## What resolves

Resolution follows the same rules Node and bundlers do:

| Feature | Supported |
| --- | --- |
| `exports` maps, including conditions and subpaths | Yes |
| Wildcard subpaths, `"./features/*"` | Yes |
| Scoped packages, `@scope/name` | Yes |
| Legacy `module` and `main` fields | Yes |
| Directory indexes and extension probing | Yes |
| Nested `node_modules`, including two versions of one package | Yes |
| CommonJS packages | Yes, wrapped automatically |
| JSON imports | Yes, as a module with the document as its default export |

Conditions are matched in the order `import`, `module`, `browser`, `default`, so a package that
ships both builds is loaded as an ES module.

Subpaths work as they do anywhere else:

```tsx
import { nanoid } from "nanoid/non-secure";
```

---

## CommonJS

A CommonJS package is wrapped so that it looks like an ES module. Its `module.exports` becomes the
default export, and its own `require` calls are turned into real imports before it runs:

```tsx
import classNames from "classnames";
```

Named imports are the one thing wrapping cannot do in general, because a CommonJS module's exports
only exist once it has run. Assignments of the form `exports.name = ...` are detected and
re-exported, and an entry point that is nothing but `module.exports = require("./cjs/thing.js")` is
followed to the module it names, which is how packages that pick a build at load time still offer
named imports. That covers most packages, but the reliable form is a default import:

```tsx
import pkg from "some-commonjs-package";
const { thing } = pkg;
```

Packages branching on `process.env.NODE_ENV` get a `process` of their own, scoped to the module, so
the branch resolves in a browser that has no such global. It reports `production`.

---

## What does not work

**Node built-ins.** Server rendering runs JavaScript in an embedded engine, not in Node. A package
that requires `fs`, `path`, `crypto` or similar fails with a message naming the module it wanted.
Browser-oriented and pure-JavaScript packages are fine, which is most of what a view needs.

**Native modules.** Anything with a `.node` binary is out for the same reason.

**Browser globals during server rendering.** A package that touches `window` or `document` at import
time cannot be server-rendered. Import it from a client-rendered view, or guard the use:

```tsx
import { isServerRender } from "dotnet:rendering";

if (!isServerRender()) {
    const { default: chart } = await import("chart.js");
}
```

---

## What gets served to the browser

Only the files a view actually reaches. Starting from the compiled views, JsxCore resolves every
bare specifier and follows the dependency graph until it closes; those files, and no others in
`node_modules`, are servable over HTTP.

Specifiers inside package files are rewritten to asset URLs rather than left to the browser. Node
lets a package import `./util` and find `./util/index.js`, and a browser will not probe for that, so
the resolution happens on the server. It also means a nested copy of a package gets its own URL and
cannot collide with the top-level one.

Package assets carry the build id like every other JsxCore asset, so they are immutable and cached
indefinitely.

In a Release build they are also minified, which matters more here than anywhere else: packages are
usually most of what a browser downloads, and they arrive as npm published them, unminified and
comment-heavy. Each module is minified on its own, so the graph the browser walks is the same graph
with the same URLs, only smaller. See
[Build and deploy](build-and-deploy.md#minification-and-compression).

### Preferring a CDN

Entries in `options.ImportMap` win over the generated ones, so a package can be pointed elsewhere
without uninstalling it:

```csharp
options.ImportMap["chart.js"] = "https://esm.sh/chart.js@4";
```

### Turning it off

```csharp
options.AllowNodeModules = false;
```

Server rendering then rejects bare imports with an explanatory error, no import map entries are
generated, and the npm asset route is not registered.

---

## Deploying

Server rendering reads packages from `node_modules` at run time, and the browser is served them from
the same place, so they have to exist on the server. Two ways:

**Restore on the server** from the committed lock file, with npm or without it:

```bash
dotnet npm ci        # or: npm ci --omit=dev
```

**Or let publish carry them**, which happens automatically for everything in `dependencies`. Those
package directories are copied under `node_modules/` in the publish output, where the runtime finds
them by walking up from the content root. `devDependencies` are left out, which is the same cut a
production `npm ci` makes.

Views that use no packages need neither: see [Build and deploy](build-and-deploy.md) for the
precompiled path.

---

## See also

- [Package management](package-management.md) for installing, the `dotnet npm` tool, and lock files
- [Writing views](writing-views.md) for what a view can and cannot do
- [Extensibility](extensibility.md#add-module-specifiers) for import map entries
- [Build and deploy](build-and-deploy.md) for publish modes
