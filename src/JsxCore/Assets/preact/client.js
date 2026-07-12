// Client entry for Preact mode.

import { createElement, render, hydrate } from "preact";

export function mountView(Component, options) {
    const settings = options || {};
    const containerId = settings.containerId || "jsxcore-root";
    const modelId = settings.modelId || "jsxcore-model";

    const container = document.getElementById(containerId);
    if (!container) {
        throw new Error("JsxCore: no container element with id '" + containerId + "' was found.");
    }

    const modelScript = document.getElementById(modelId);
    const model = modelScript && modelScript.textContent ? JSON.parse(modelScript.textContent) : null;
    const context = window.__jsxcore_context || {};

    const element = createElement(Component, { model, context });

    // When the server already produced this markup, attach to it rather than discarding it. This
    // is real hydration: existing DOM nodes are reused and only event handlers are wired up.
    if (settings.hydrate) {
        hydrate(element, container);
    } else {
        render(element, container);
    }

    window.__jsxcore_root = {
        model,
        context,
        createElement,
        root: { render: (next) => render(next, container) },
        // Preact re-renders by rendering into the same container again. The element has to be one
        // Preact understands, which is why building it belongs here and not in the reload client.
        update: (next) => render(createElement(next, { model, context }), container)
    };

    return window.__jsxcore_root;
}

export { createElement, render, hydrate };
