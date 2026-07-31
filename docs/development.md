# Development

← [Documentation index](README.md)

---

## Hot reload

Enabled automatically in the Development environment. JsxCore watches the views directory,
recompiles on change, and pushes the result over a WebSocket at `/_jsx/hmr`.

```csharp
builder.AddJsxCore(options =>
{
    options.HotReload = true;        // null (default) = on in Development only
    options.WatchForChanges = true;  // null (default) = on in Development only
});
```

`UseJsxCore()` adds `UseWebSockets()` for you when hot reload is on. Call it **before**
`UseRouting()` so asset requests short-circuit early.

### How an update is applied

Compiled modules are served under a build-id path segment, so applying an update is just
re-importing the view from the new prefix. The whole module graph refreshes with no cache-busting
of individual imports. Client-rendered views re-render in place; component state is lost, but the
page is not reloaded.

If the module cannot be re-imported for any reason, the client falls back to a full page reload,
so you never end up looking at stale output without knowing.

### When compilation fails

A failed compile shows an error overlay with the TypeScript diagnostics, rather than silently
serving the last good build:

```
TypeScript compilation failed

Views/Home/Index.tsx(12,7): error TS2322: Type 'string' is not assignable to type 'number'.
```

The overlay clears on the next successful build.

### Server-rendered views

A rebuild discards the pooled JavaScript engines, so the next server-rendered request picks up the
new code. There is no in-place hot update for server rendering. The page reloads.

---

## Which framework is being served

Every response carries a header in Development, so the answer is in the network tab rather than in
the project file:

```
X-JsxCore-Framework: preact
```

It reports what the running application is actually serving, which is the point: the framework is
chosen by the build, so a project file edited without a rebuild, or output published from a
different configuration, shows up here as a disagreement with what you expected. The header is not
written outside Development.

---

## Editor support

TypeScript-aware editors resolve imports using the nearest `tsconfig.json` to the file being
edited. JsxCore's own compiler config lives under `obj/`, where editors will not find it, so
without help Rider and VS Code flag every `@jsxcore/runtime` import as unresolved even though
compilation succeeds.

JsxCore therefore writes a small `tsconfig.json` **beside your views**:

```jsonc
{
  "//": "jsxcore-generated: written by JsxCore so editors can resolve '@jsxcore/runtime'. ...",
  "compilerOptions": {
    "jsx": "react-jsx",
    "jsxImportSource": "preact",
    "noEmit": true,
    "paths": {
      "@jsxcore/runtime": ["../obj/JsxCore/runtime/index.d.ts"],
      "@jsxcore/generated": ["../obj/JsxCore/types/index.d.ts"],
      "preact": ["../node_modules/preact/src/index.d.ts"],
      "react": ["../node_modules/preact/compat/src/index.d.ts"]
    }
  },
  "include": ["**/*"]
}
```

Every path in it is **relative**, so it is safe to commit, and you should, because it makes the
editor work for everyone on the team.

It is produced by the MSBuild target (so a fresh clone works after `dotnet build`) *and* at
application startup, and it is derived from the same base configuration as the real compiler
config, so the two cannot disagree.

### Taking it over

Just edit it. JsxCore only overwrites a file that still contains its `jsxcore-generated` marker
comment, so removing that comment makes the file yours permanently.

To stop it being written at all:

```csharp
options.GenerateEditorTsConfig = false;
```
```xml
<JsxCoreGenerateEditorTsConfig>false</JsxCoreGenerateEditorTsConfig>
```

---

## Where generated files live

| Path | What | Commit? |
|---|---|---|
| `obj/JsxCore/js/` | Compiled views | No |
| `obj/JsxCore/types/index.d.ts` | Types generated from your .NET models | No |
| `obj/JsxCore/runtime/` | Runtime type declarations, for the compiler | No |
| `obj/JsxCore/preact/` | Preact, staged from the JsxCore package or node_modules | No |
| `obj/JsxCore/tsconfig.json` | The config the compiler actually uses | No |
| `Views/tsconfig.json` | Editor support | **Yes** |
| `package.json` | npm manifest, created by the bootstrap if absent | **Yes** |
| `package-lock.json` | Pinned package versions | **Yes** |
| `node_modules/` | Installed packages | No |

Everything under `obj/` is covered by the standard .NET `.gitignore` already. The two manifests are
worth committing because they pin your package versions and let the build restore exactly what is
pinned
rather than resolving afresh.

---

## Watching

The watcher covers the views directory recursively, debounces bursts (editors write in several
passes), and ignores the generated `tsconfig.json` so its own output cannot trigger a rebuild loop.

Changes to **.NET** code are not watched; that is `dotnet watch`'s job. Note that generated model
types are produced at startup, so changing a C# model needs an application restart to be reflected
in TypeScript.

---

## Continuous integration

`.github/workflows/build.yml` builds and tests on Linux and Windows for every push and pull request
to `main`, and packs both packages on Linux afterwards, checking that the dotnet tool installs and
runs from what it just packed.

The suite needs nothing but the .NET SDK. It restores the repository's npm packages with JsxCore's
own client before the first test, so a clean checkout runs green on a machine with no Node at all.
CI installs Node anyway, because a couple of tests hand a lock file JsxCore wrote to real `npm ci`
and check that npm accepts it and installs from it. Those skip themselves when npm is absent, and a
check that passes by not running is worth nothing on the machine meant to catch the difference.

`.github/workflows/release.yml` publishes to NuGet when a GitHub release is published. The release
creates the tag, and the tag is the version: `1.4.0` or `v1.4.0` publishes 1.4.0, and a tag that is
not a version NuGet accepts fails the run before anything is pushed. It runs the tests again on the
released commit, packs, verifies the tool installs, pushes both packages, and attaches them to the
release.

It needs a `NUGET_API_KEY` repository secret, and an environment named `nuget`. Give that
environment a required reviewer to put an approval between the release and the push, which is worth
doing: a version on NuGet can be unlisted but never replaced.
