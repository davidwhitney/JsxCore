# Tailwind CSS

← [Documentation index](README.md)

Tailwind works, and needs ~15 lines of setup. There is a working example in
[`samples/SampleApp.Tailwind`](../samples/SampleApp.Tailwind).

JsxCore does not process CSS. Tailwind integration is therefore an ordinary build step that produces a stylesheet, and the stylesheet is an ordinary static file.

---

## Setup

**1. Declare Tailwind.** JsxCore restores it like anything else in `package.json`, without npm:

```json
{
  "devDependencies": {
    "@tailwindcss/cli": "^4.3.3",
    "tailwindcss": "^4.3.3"
  }
}
```

A dev dependency, because what reaches production is the compiled stylesheet, not the compiler.

**2. Write the stylesheet.** Tailwind v4 is configured in CSS rather than in a JavaScript config
file:

```css
/* Styles/app.css */
@import "tailwindcss";

/* Where to look for class names. */
@source "../Views";
```

`@source` matters here. Tailwind scans for class names to decide what to emit, and your markup is in
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

Then write classes as you would anywhere:

```tsx
<button class="rounded-lg bg-indigo-500 px-4 py-2 font-medium hover:bg-indigo-400 transition">
    Clicked {count} times
</button>
```

`class` and `className` are both accepted.

---

## Tailwind needs Node

**JsxCore installs Tailwind for you without npm.** The packages come from the registry through
JsxCore's own client, like everything else.

**Running Tailwind's CLI needs Node**, because it is a Node program. So a machine that builds a
Tailwind project needs Node installed, even though a machine that builds a plain JsxCore project
does not.

The `ContinueOnError="true"` above is deliberate. A machine without Node still builds and runs; it
serves the stylesheet from the last time the CLI did run, which is usually the committed one. Drop
it if you would rather the build fail loudly.

If you want to avoid the Node dependency entirely, the options are to commit the compiled
stylesheet, to compile it in CI where Node is present, or to use Tailwind's browser build, which
compiles in the page and is not intended for production.

---

## Hot reload

Editing a view hot reloads the view, but not the stylesheet: the CLI runs at build time, so a new
class name needs a rebuild. Running the CLI in watch mode beside the app is the usual answer:

```bash
node node_modules/@tailwindcss/cli/dist/index.mjs -i Styles/app.css -o wwwroot/app.css --watch
```

That writes `wwwroot/app.css` as you type; the browser picks it up on the next reload.

---

## See also

- [Writing views](writing-views.md): `class` and `className`, the JSX dialect
- [Build and deploy](build-and-deploy.md): what publish carries, and minification
- [npm packages](npm-packages.md): how packages are restored and served
