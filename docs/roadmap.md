# Roadmap

← [Documentation index](README.md)

---

Three gaps. Restoring packages without npm is mostly closed and the rest is listed below. .NET stops
being callable at the boundary between server rendering and the browser. And styling is left
entirely to you, which is the one place JsxCore currently says "not my problem" about something
every application needs.

None is scheduled. This is a record of what they involve, including the parts that are hard, so the
work can be judged before it is started.

---

## 1. Restore npm packages without npm

**This is now the default.** `NativePackageManager` creates a package.json, installs a dependency
graph into node_modules, and writes a lock file that real `npm ci` accepts, with nothing installed
but the .NET SDK. Version resolution is checked against npm's own semver package rather than against
a reading of the specification, and the trees it produces were compared against npm's while it was
being built. npm remains
available behind `JsxCoreUseNpm`.

That closes the last external tool JsxCore required. No Node process ran during a build or a request
before, and a published application never needed one; now nothing needs one at any point.

What is built, and what is not, is marked below.

### What it would do

**Resolve.** *(built)* Read `package.json`, fetch each packument from the registry, pick a version
for every range, and walk the result until the graph closes.

**Fetch.** *(built)* Download each tarball, verify its `dist.integrity` hash, and extract it into
`node_modules`. Archives cannot write outside the directory they are unpacked into.

**Record.** *(built)* Write `package-lock.json` at `lockfileVersion` 3, with the `resolved` URL and
`integrity` hash for every package. Real `npm ci` accepts what we write, installs from it, and
leaves it unrewritten. For the same input our output agrees with npm's on every path, version,
resolved URL and integrity hash; npm additionally records `license` and `peerDependenciesMeta`,
which we do not, so the files are equivalent rather than byte identical.

**Restore.** *(built)* Install exactly what a lock file pins, resolving nothing. Platform specific
entries are filtered at this point rather than at resolution, which is what lets one lock file serve
every platform.

**Author.** *(built)* Create `package.json` when there is not one, and add entries when a package
is installed, which is what the build already asks npm to do.

### The hard parts

**Semver ranges.** *(built)* `^`, `~`, `x`, hyphen ranges, `||`, and the prerelease rules, which
are the subtle ones: `1.0.0-beta` satisfies `^1.0.0-alpha` but not `^1.0.0`. Checked against npm's
own semver package for every form, which is how the one disagreement was found: `^1` was being read
as `<1.1.0` rather than `<2.0.0`.

**Tree layout.** *(built)* A package is hoisted to the top level unless something incompatible is
already visible from where it is needed, in which case it goes to the shallowest scope that is free.
Trees were matched against npm's for eslint, webpack and jest until they were identical: same
packages, same paths, same versions, 325 of them in jest's case.

Matching npm meant matching its order, because ordering is the whole of the hoisting policy:
whichever dependent asks first claims the top level slot. Three rules turned out to decide it.
Nodes are expanded shallowest first and alphabetically within a depth, which is how npm sorts its
own queue. A peer is claimed at the moment its dependent is placed rather than when that dependent
is later expanded, which decides which version of a contested peer reaches the top. And an optional
peer is not followed at all, because it exists to pin a version rather than to bring a package in.

Independently of npm, there is a check that every dependency of every placed package resolves, from
where that package sits, to a version that satisfies it. Installing refuses to proceed if it fails.
That check earned its place: it caught a real bug where peers were placed but never expanded, so
their own dependencies were missing from the tree. npm hoists when nothing
conflicts and nests when something does, and the lock file describes that layout by path, so
reproducing the layout and reproducing the lock file are the same problem. This remains the part
most likely to be subtly wrong.

**Platform filtering.** *(built)* Not an edge case here. The TypeScript compiler is delivered as 20
optional dependencies, one per platform, selected by `os` and `cpu`. There is a trap in it: those
packages are listed in both `dependencies` and `optionalDependencies`, and npm treats the optional
entry as overriding the other. Honouring only the first block installs all twenty.

**Workspaces, overrides and git dependencies.** *(built)* A workspace is symlinked rather than
copied and its dependencies resolve as though the root declared them, which puts one shared copy at
the top instead of one inside each. `overrides` replace a range wherever it appears, including
transitively, and the soundness check runs against the overridden range rather than the original, or
every override would look like a broken tree. Git dependencies are fetched as host archives, so
there is no `git` binary involved either.

**Registry configuration.** *(not built)* The registry URL is configurable and nothing else is.
`.npmrc` carries scoped registries, auth tokens and proxy settings. An organisation behind a private registry needs all of it, and credentials must be
read without ever being written into a lock file or a log.

**Peer dependencies.** *(built)* npm 7 and later install them automatically, and they change the
tree: a peer claims its slot when its dependent is placed, which is early enough to win the top
level from a regular dependency that would otherwise have taken it.

### What it deliberately would not do

**Lifecycle scripts.** `preinstall`, `install` and `postinstall` are Node programs, and running them
would reintroduce the dependency this removes. Packages that need them, which in practice means
packages with native builds, are out of scope. A package declaring one should be reported clearly
rather than half-installed. Nothing JsxCore itself needs runs scripts.

**Being npm.** The goal is restoring a declared dependency set reproducibly. `npm publish`,
`npm audit`, `npm dedupe` and the rest are not in scope.

**Peer dependencies.** *(built)* Installed automatically as npm 7 and later do, placed beside the
dependent rather than underneath it, and skipped when marked optional.

### How we would know it works

The bar is not "it installed something", it is "npm agrees with what we did".

- Round trip: *(done)* restore with our client, then run `npm ci` against the lock file we wrote on
  a machine that has npm. Covered for a small graph and for webpack, which is large enough to need
  nesting. npm accepts both and installs from them.
- Soundness: *(done)* every dependency of every placed package resolves to a satisfying version from
  where it sits. Asserted for eslint and jest, and enforced before anything is written.
- Parity: *(done once, not guarded)* whole trees were compared against ones npm generated at the
  time, for three graphs of increasing size, and matched. It is no longer run on every build: npm's
  answer is not checked in, because these graphs float and a recorded one describes a tree that
  stops existing as soon as any of 300 packages publishes, so the comparison eventually fails on
  somebody else's release rather than on a change here. The last difference it found was three
  packages nested under an optional platform binding that cannot run on the test machine, which
  npm prunes and we expand.
- Corpus: for a set of real dependency sets, compare our resolution against `npm install --dry-run
  --json` and diff the chosen versions.
- Determinism: the same inputs produce a byte-identical lock file, twice, on each platform.
- Integrity: every tarball is checked against its published hash, and a mismatch fails loudly.

### Where it plugs in

The seam is built. Restoring goes through `IPackageManager`, with a selector that asks each strategy
in turn whether it can run:

```csharp
public interface IPackageManager
{
    string Name { get; }
    bool IsAvailable();
    PackageOperationResult CreateManifest(string directory);
    PackageOperationResult RestoreFromLockFile(string directory);
    PackageOperationResult InstallDeclared(string directory);
    PackageOperationResult Add(string directory, IReadOnlyCollection<PackageRequest> packages);
}
```

Both callers, the build and application startup, go through it, and the policy of what to restore
lives in `DependencyRestorer` rather than in either of them. `NativePackageManager` is first in the
list and `NpmPackageManager` second, so naming npm is the only thing that reaches npm.

### A dotnet tool for installing

*(built)* The same client is reachable without a build, as a global tool:

```bash
dotnet tool install -g JsxCore.Npm
dotnet npm add marked --version ^12
dotnet npm restore
```

Shaped after `dotnet package add` rather than after npm, because the people using it are already
holding the .NET CLI: `add`, `remove`, `list`, `restore`, `init`, with `--version`, `--dev` and
`--project`. npm's own `marked@^12` and `install` spellings are accepted as well.

The executable is called `dotnet-npm`, which is what makes `dotnet npm` resolve to it. Naming it
`npm` would put an `npm` on PATH and shadow the real one, which is an unusually rude thing for a
tool whose selling point is not needing npm.

Two reasons it is worth having beyond the build integration. It gives the restore logic somewhere to
be exercised directly, which matters for something whose correctness bar is "npm agrees with what we
did": a corpus comparison is a script over a command, not a test harness around MSBuild. And it
covers the case the build cannot, which is wanting to add a package without running a build, or on a
machine that has the .NET SDK and no Node at all.

It is the same `IPackageManager` implementation with a command line over it, not a second
implementation, packed from the same project the build tool is packed from. Using it found a real
bug the build never hit: adding a second package resolved only that package, so the lock file was
rewritten describing one dependency instead of all of them, and `npm ci` rejected it as out of sync.

### Migration

*(done)* The native client is first in the list, having passed the round trip: npm accepts and
installs from the lock files it writes. `<JsxCoreUseNpm>true</JsxCoreUseNpm>` puts npm back, which
is the answer for a private registry needing `.npmrc` authentication or a dependency with install
scripts, since neither is supported natively.

---

## 2. Call .NET from the browser, not just from the server

Server-rendered views can call .NET objects directly, in process, with no bridge:

```tsx
export default function Dashboard() {
    return <p>{dotnet.Inventory.getSummary().total}</p>;
}
```

Client-rendered views cannot, and the documentation tells you to guard the call:

```tsx
const summary = isServerRender() ? dotnet.Inventory.getSummary() : null;
```

That asymmetry is the sharpest edge in the model. The same view, the same call, and it works or does
not depending on where it runs.

The proposal is to compile the exported .NET surface to WebAssembly, load it in the browser, and
generate TypeScript shims so that the call above means the same thing on both sides.

### What it would do

**Export.** Types are chosen the way model types already are, by scanning a namespace or by
attribute, so there is one mechanism to learn rather than two.

**Compile.** Build the exported assembly to WebAssembly through the `wasm-tools` workload, trimmed
to what is reachable from the exported surface.

**Generate.** Extend the existing TypeScript generator to emit callable declarations, not only
interfaces. It already produces `Verify.Models.PageModel`; it would also produce
`dotnet.Inventory.getSummary(): Summary`, typed against those same models.

**Marshal.** `[JSExport]` handles primitives, strings and tasks. Anything else crosses as JSON,
using the same `JsonSerializerOptions` the view engine already uses for models, so a type looks the
same whether it arrived in the model or came back from a call.

### The hard parts

**Payload.** The .NET WebAssembly runtime is measured in megabytes. JsxCore's client runtime is
under 7 KB gzipped, and that is a large part of why it is pleasant. This has to be opt in, loaded
lazily, and entirely absent from any page that does not call .NET. If that cannot be guaranteed, the
feature is not worth having.

**Which assembly.** An application assembly references ASP.NET Core and will not run in WebAssembly.
The exported surface has to live somewhere that can, which in practice means a separate project.
That is a real constraint on users and should be designed for openly rather than discovered.

**Synchronous or not.** Server rendering is synchronous by design, and a component that returns a
promise is rejected. Loading a WebAssembly module is asynchronous. Either the browser API becomes
async, and the two sides no longer look the same, which loses the entire point; or hydration waits
for the module so calls can stay synchronous, and pages that use .NET hydrate later. The second
preserves the property worth having, and is the one to design for.

**Trimming.** Reflection-based serialisation does not survive trimming, so the generated surface
needs source-generated serialisers. This is tractable and known, but it is work.

**Another toolchain.** This removes a boundary by adding a .NET workload, in the same release cycle
as an item that removes a Node dependency. That is a fair trade only if it stays opt in: a project
that does not use the bridge should not need `wasm-tools` installed.

### How we would know it works

- The same method, called from a server-rendered view and a client-rendered one, returns the same
  value for a corpus of return types including records, collections, enums and nullables.
- The generated shims type check against the generated model types, in the same compilation.
- A view that does not call .NET downloads no WebAssembly, asserted on what the page actually
  requests.
- A trimmed build round trips every exported type, which is where reflection-based serialisation
  fails silently.

---

## 3. CSS processing

Views compile, model types generate, packages restore, and stylesheets are on their own. Today you
write a `<link>` into `options.Document.HeadContent`, put a file in `wwwroot`, and any processing is
a separate toolchain you run yourself.

That is a real gap. A component-shaped view engine where styles are not component-shaped is only
half the idea, and PostCSS, with Tailwind on top of it, is what most people reach for.

### What it would do

**Process on the same schedule as views.** A stylesheet is an input like a `.tsx` file: compiled on
build, recompiled by the watcher, fingerprinted into the build id so its URL is immutable, and
carried into publish output. None of that machinery would be new, which is the argument for doing it
here rather than telling people to run two watchers.

**Import stylesheets from views.** `import "./Card.css"` beside `Card.tsx`, with the engine
collecting what a rendered view actually reached and emitting only those links. That is the same
graph walk the npm client asset serving already does.

**Stay a strategy, not a hard dependency.** PostCSS is a Node program, and JsxCore's entire premise
is not needing one. So the processor is an interface with more than one implementation, the same
shape as package management: a pass-through that copies and fingerprints, needing nothing; a PostCSS
processor for projects that have Node and want Tailwind; and room for a native one later.

### The hard parts

**PostCSS needs Node.** Not the compiler-shaped kind that can be shipped as a native binary: it is a
plugin ecosystem, and the plugins are JavaScript. Running Tailwind means running Node, which
contradicts the premise. The honest framing is that this is opt in, and choosing it means accepting
a Node dependency the rest of JsxCore does not have. Tailwind's own standalone CLI is a single
native binary and sidesteps this for the common case, which makes it the more interesting first
target of the two.

**Scoping.** Component-scoped styles mean rewriting class names in both the stylesheet and the
markup that references them, which reaches into the JSX transform. Worth doing, and much larger than
copying files.

**Ordering.** CSS is order dependent in a way ES modules are not. Two views importing the same
stylesheet in different orders must not produce different results, so emission order has to be
derived from the graph rather than from render order.

**When it runs.** Views are compiled by a native binary in tens of milliseconds. A Node-based CSS
pipeline is slower than that, and putting it in the same synchronous startup path would undo the
fast feedback loop. It probably belongs on the watcher's schedule, not in the request path.

### How we would know it works

- A view importing a stylesheet renders a link to it, and a view that does not import it does not.
- Changing a stylesheet moves the build id, and therefore the URL, exactly as changing a view does.
- Publish output contains the processed stylesheet and no source, and the application serves it with
  no processor installed.
- With the pass-through processor selected, nothing requires Node.

---

## Order

Items 1 and 3 both avoid an external tool and share the same strategy pattern, so the first of them
to be built makes the second cheaper. Item 1 is the safer place to start: it has a correctness bar
that can be checked against npm itself, it removes a requirement rather than adding one, and it can
ship behind a switch until the comparison passes.

Item 2 changes the programming model. It is worth more, and it should not start until the
synchronous-or-not question above has an answer that survives contact with a real application.
