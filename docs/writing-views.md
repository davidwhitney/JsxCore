# Writing views

← [Documentation index](README.md)

---

## The view contract

A view is a module with a **default export** that is a component function. It receives `model` and
`context`:

```tsx
import type { ViewProps } from "dotnet:rendering";

interface Model { title: string; count: number }

export default function Page({ model, context }: ViewProps<Model>) {
    return <h1>{model.title} ({model.count}) at {String(context.path)}</h1>;
}
```

| Prop | What it is |
|---|---|
| `model` | Whatever you passed to the result, serialised with your `JsonSerializerOptions` |
| `context` | Ambient values: `path`, plus anything from `ContextValues` or `AddJsxContext` |

Don't hand-write the model interface. [Generate it from your C#](model-types.md) instead.

---

## Importing components

Plain ESM. Write the real extension; TypeScript rewrites it for the browser:

```tsx
import { Card, Nav } from "../Shared/Layout.tsx";
import { formatMoney } from "./money.ts";
```

Components can live anywhere under the views directory. There is no registration step, no barrel
file to maintain and no bundler config. A component is just an export:

```tsx
// Views/Shared/Layout.tsx
import type { ComponentChildren } from "preact";

export function Card({ title, children }: { title: string; children?: ComponentChildren }) {
    return (
        <section class="card">
            <h2>{title}</h2>
            {children}
        </section>
    );
}
```

Only the **view** needs a default export. Shared components can export whatever they like.

---

## The `head` export

Populates the document head. It can be an object or a function of the model:

```tsx
export const head = { title: "Dashboard" };
```

```tsx
export const head = (model: Model) => ({
    title: `${model.name} | Dashboard`,
    meta: [{ name: "description", content: model.summary }],
    links: [{ rel: "canonical", href: model.url }],
    scripts: [{ src: "/analytics.js", defer: "true" }]
});
```

`meta`, `links` and `scripts` are lists of attribute bags; whatever keys you provide become
attributes.

It works in **every render mode**. For client-rendered views JsxCore evaluates just this export on
the server, without running the component, so the document still gets a proper title without
shipping the page first.

A [per-response `Title`](returning-views.md#per-response-document-settings) overrides it when you
need to.

---

## Hooks

The full set, from `preact/hooks`: `useState`, `useEffect`, `useRef`, `useMemo`, `useCallback`,
`useReducer`, `useContext` and the rest. In [React mode](runtimes.md) they come from `react`
instead; nothing else about a view changes.

```tsx
import { useState } from "preact/hooks";

export default function Counter() {
    const [count, setCount] = useState(0);
    return <button onClick={() => setCount(count + 1)}>{count}</button>;
}
```

During server rendering hooks return their initial values and effects never run, the same
behaviour you would get from any SSR pass. Calling a setter during a server render throws, because
there is no second render to apply it to.

---

## The JSX dialect

Close to React, with two added conveniences:

| | Accepted |
|---|---|
| CSS classes | `class` **and** `className` |
| Label targets | `for` **and** `htmlFor` |

Everything else behaves as you would expect:

```tsx
<div
    class={isActive ? "row active" : "row"}
    style={{ marginTop: 8, color: "red" }}      // numbers get px, camelCase gets hyphenated
    data-id={item.id}                            // data-* and aria-* pass through
    onClick={() => select(item)}
>
    <input type="checkbox" checked disabled={false} />   {/* false omits the attribute */}
    <img src="/a.png" alt="" />                          {/* empty alt is preserved */}
    <>{fragments}{are}{fine}</>
    <p dangerouslySetInnerHTML={{ __html: trustedHtml }} />
</div>
```

Lists want keys, as usual:

```tsx
<ul>{items.map((item) => <li key={item.id}>{item.name}</li>)}</ul>
```

**Everything interpolated into markup is HTML-escaped.** A model value containing `<script>`
renders as text, not markup. The only way to emit raw HTML is `dangerouslySetInnerHTML`, which is
named that way for a reason.

---

## Types and IntelliSense

The runtime ships `.d.ts` files covering the JSX namespace, intrinsic elements, their attributes
and the hooks, so you get completion and type checking on `<input type=...>` as well as on your own
components.

Editors need a little help finding them; see [Development](development.md#editor-support).

---

## Static assets

JsxCore renders HTML. It does not process CSS, images or fonts. Serve those with
`UseStaticFiles()` and reference them normally:

```csharp
app.UseStaticFiles();

builder.AddJsxCore(options =>
{
    options.Document.HeadContent = "<link rel=\"stylesheet\" href=\"/site.css\">";
});
```

Or per response, with [`result.HeadContent`](returning-views.md#per-response-document-settings).

---

## What a view cannot do

- **Be async.** Server rendering is synchronous, because .NET calls return immediately and nothing
  needs awaiting. An async component is rejected with an explicit error. Do async work in your
  endpoint and pass the result in the model.
- **Use a package that needs Node.** npm packages work in views, on the server and in the browser
  (see [Using npm packages](npm-packages.md)), but the server runs them in an embedded engine with
  no `fs`, `path` or other Node built-ins. A package that reaches for one fails with a message
  naming it.
- **Reach outside the views directory.** A relative import that resolves outside the compiled
  output is refused.
