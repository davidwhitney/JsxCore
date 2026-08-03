# Styling

← [Documentation index](README.md)

Every way to get CSS onto a page: plain stylesheets, stylesheets beside a component, CSS Modules,
package styles, and Tailwind.

---

## Which to use

| | Reach for | Where the file lives |
|---|---|---|
| One stylesheet for the whole application | `options.Document.HeadContent` | `wwwroot` |
| A stylesheet a page or component owns | an import | beside the component, or `wwwroot` |
| Styles that must not collide with anyone else's | a CSS module | beside the component |
| Utility classes | [Tailwind](#tailwind) | generated into `wwwroot` |
| A third-party component's own styles | an import | its npm package |

There is no bundler, so nothing is concatenated. Each stylesheet is served as a stylesheet, and the
document links the ones the page actually reaches.

---

## One stylesheet for everything

The simplest thing, and often the right one. Put it in `wwwroot`, serve it, and name it once:

```csharp
app.UseStaticFiles();

builder.AddJsxCore(options =>
{
    options.Document.HeadContent = """<link rel="stylesheet" href="/site.css">""";
});
```

Or per response, with [`result.HeadContent`](returning-views.md#per-response-document-settings).

Nothing about this involves JsxCore: it is a static file and a link element.

---

## Stylesheets a component owns

Import one and every page that reaches it gets a `<link>` in its head, in every render mode:

```tsx
import "./card.css";

export function Card() {
    return <div class="card" />;
}
```

The import binds nothing, which is why it is answered in the document rather than at run time. Three
places one can come from, and the spelling says which:

```tsx
import "/css/site.css";           // your web root, served by UseStaticFiles
import "./card.css";              // beside the component
import "some-widget/styles.css";  // an npm package's own styles
```

A stylesheet in the web root keeps its own URL, because the application already serves it and one
file should keep one URL. The other two are build outputs, so they are served from the build's own
prefix like a compiled module, and change URL when their contents change.

---

## CSS Modules

Name a stylesheet `*.module.css` and its class names are scoped, so two components can both use
`.card` without colliding:

```css
/* Card.module.css */
.card { padding: 24px; }
.title { font-weight: 600; }
```

```tsx
import styles from "./Card.module.css";

export function Card({ title }: { title: string }) {
    return (
        <div class={styles.card}>
            <h2 class={styles.title}>{title}</h2>
        </div>
    );
}
```

`styles.card` is the scoped name the stylesheet was rewritten to use, so the markup and the CSS
cannot disagree. `:global(...)` opts a selector out of scoping, and `composes` works.

TypeScript types the default export as a map of strings, so a typo is `undefined` rather than a
class that silently does nothing.

### How it works, and what it costs

The scoping is [esbuild's](https://esbuild.github.io), the same native binary JsxCore already
restores to minify with. So CSS Modules need no Node, no PostCSS and nothing installed beyond what a
Release build uses anyway.

Every stylesheet in the application goes through one esbuild invocation. That is what lets two
components that both have a `Card.module.css` get distinct class names, so it is not an optimisation
that could be dropped.

One consequence: **esbuild has to be present**, including in Debug, where it used to be restored
only for Release builds. The build restores it; if it is missing, JsxCore says so rather than
serving stylesheets whose class names mean nothing.

---

## Tailwind

Tailwind works and needs about fifteen lines of setup. There is a working example in
[`samples/SampleApp.Tailwind`](../samples/SampleApp.Tailwind).

JsxCore processes the stylesheets views import, but it does not run PostCSS, which is what Tailwind
is built on. So Tailwind is an ordinary build step producing a stylesheet, and that stylesheet is
then an ordinary import.

**1. Declare it.** JsxCore restores it like anything else in `package.json`, without npm:

```json
{
  "devDependencies": {
    "@tailwindcss/cli": "^4.3.3",
    "tailwindcss": "^4.3.3"
  }
}
```

A dev dependency, because what reaches production is the compiled stylesheet, not the compiler.

**2. Write the stylesheet.** Tailwind v4 is configured in CSS rather than a JavaScript config file:

```css
/* Styles/app.css */
@import "tailwindcss";

/* Where to look for class names. */
@source "../Views";
```

`@source` matters. Tailwind scans for class names to decide what to emit, and the markup is in
`.tsx` files under `Views/`, which is not where it would look by default.

**3. Compile it during the build:**

```xml
<Target Name="BuildTailwindStylesheet"
        DependsOnTargets="JsxCoreEnsureDependencies"
        AfterTargets="JsxCoreCompileViews"
        BeforeTargets="Build">
  <Exec Command="node node_modules/@tailwindcss/cli/dist/index.mjs --input Styles/app.css --output wwwroot/app.css --minify"
        WorkingDirectory="$(MSBuildProjectDirectory)"
        ContinueOnError="true"
        StandardOutputImportance="low" />
</Target>
```

**4. Serve and link it:**

```csharp
builder.AddJsxCore(options =>
{
    options.Document.HeadContent = """<link rel="stylesheet" href="/app.css">""";
});

app.UseStaticFiles();
```

Then write classes as you would anywhere. `class` and `className` are both accepted.

### Tailwind needs Node

**JsxCore installs Tailwind without npm**, because the packages come from the registry through
JsxCore's own client like everything else.

**Running Tailwind's CLI needs Node**, because it is a Node program. So a machine that builds a
Tailwind project needs Node installed, even though a machine that builds a plain JsxCore project
does not.

The `ContinueOnError="true"` above is deliberate. A machine without Node still builds and runs,
serving the stylesheet from the last time the CLI did run, which is usually the committed one. Drop
it to make the build fail loudly instead.

To avoid the Node dependency entirely: commit the compiled stylesheet, compile it in CI where Node
is present, or use Tailwind's browser build, which is not intended for production.

### Hot reload

Editing a view hot reloads the view, but not the stylesheet: the CLI runs at build time, so a new
class name needs a rebuild. Run the CLI in watch mode beside the app:

```bash
node node_modules/@tailwindcss/cli/dist/index.mjs -i Styles/app.css -o wwwroot/app.css --watch
```

---

## Sass and PostCSS

The same shape as Tailwind: an external tool producing a stylesheet, which is then an ordinary
import. JsxCore runs neither, so both need whatever their CLI needs, which is usually Node.

---

## Ordering

Order follows the import graph, not the order things rendered in. A component's stylesheet is
emitted before that of the page importing it, so the page can override it, and two pages sharing a
stylesheet cannot produce two different cascades.

That order is computed from the compiled module graph, so it is the same in development and in
production. Nothing has to be checked against a production build to find out what it will be.

---

## When changes show up

Editing a stylesheet in `wwwroot` takes effect immediately, since it is a static file the browser
refetches.

Editing a processed stylesheet, or adding or removing an import, needs a rebuild. Hot reload swaps
modules; the document head is written by the server.

---

## See also

- [Import syntax](import-syntax.md#stylesheets): the specifier forms, alongside every other import
- [Writing views](writing-views.md): the view contract and the JSX dialect
- [Build and deploy](build-and-deploy.md#minification-and-compression): what minification covers
