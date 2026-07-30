// Server entry. Loaded by the .NET host, which calls renderView/readHead and reads the JSON they
// return; nothing above this layer knows how a view is rendered.

import { createElement } from "preact";
import { render } from "preact-render-to-string";

function resolveHead(viewModule, props) {
    const head = typeof viewModule.head === "function"
        ? viewModule.head(props.model, props.context)
        : (viewModule.head || null);
    return head || null;
}

// Rendering is synchronous, so a component returning a promise never resolves into markup, and
// rendering it anyway drops the component silently.
function synchronous(Component) {
    return function (props) {
        const rendered = Component(props);

        if (rendered && typeof rendered.then === "function") {
            throw new Error(
                "JsxCore: server rendering is synchronous, but a component returned a Promise. " +
                "Fetch on the .NET side and pass the result in as the model, or render this view " +
                "on the client.");
        }

        return rendered;
    };
}

export function renderView(viewModule, props) {
    const Component = viewModule.default;
    if (typeof Component !== "function") {
        throw new Error("JsxCore: a view must have a default export that is a component function.");
    }

    const element = createElement(synchronous(Component), { model: props.model, context: props.context });

    return JSON.stringify({
        html: render(element),
        head: resolveHead(viewModule, props)
    });
}

export function readHead(viewModule, props) {
    return JSON.stringify({ html: "", head: resolveHead(viewModule, props) });
}
