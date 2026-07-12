// Server entry for Preact mode. Loaded by the .NET host, which calls renderView/readHead exactly
// as it does for the built-in runtime. The JSON contract is identical, so nothing above this
// layer needs to know which runtime is in use.

import { createElement } from "preact";
import { render } from "preact-render-to-string";

function resolveHead(viewModule, props) {
    const head = typeof viewModule.head === "function"
        ? viewModule.head(props.model, props.context)
        : (viewModule.head || null);
    return head || null;
}

export function renderView(viewModule, props) {
    const Component = viewModule.default;
    if (typeof Component !== "function") {
        throw new Error("JsxCore: a view must have a default export that is a component function.");
    }

    return JSON.stringify({
        html: render(createElement(Component, { model: props.model, context: props.context })),
        head: resolveHead(viewModule, props)
    });
}

export function readHead(viewModule, props) {
    return JSON.stringify({ html: "", head: resolveHead(viewModule, props) });
}
