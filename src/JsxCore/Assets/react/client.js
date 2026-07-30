// Client entry for React.

import React from "react";
import ReactDomClient from "react-dom/client";

const { createElement } = React;
const { createRoot, hydrateRoot } = ReactDomClient;

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

    // hydrateRoot takes the markup the server produced and attaches to it; createRoot replaces
    // whatever is there. React insists on knowing which at creation time, unlike Preact, so the
    // root has to be built differently rather than rendered into differently.
    const root = settings.hydrate
        ? hydrateRoot(container, element)
        : (() => { const created = createRoot(container); created.render(element); return created; })();

    window.__jsxcore_root = {
        model,
        context,
        createElement,
        root,
        // Re-rendering is the same root with a new element. Building it here keeps the reload
        // client free of any knowledge of which framework is mounted.
        update: (next) => root.render(createElement(next, { model, context }))
    };

    return window.__jsxcore_root;
}

export { createElement };
