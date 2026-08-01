// Client entry for React. The framework-specific half of mounting a view; the rest is shared.

import React from "react";
import ReactDomClient from "react-dom/client";
import { createMountView } from "./view-host.js";

const { createElement } = React;
const { createRoot, hydrateRoot } = ReactDomClient;

// React decides hydration when the root is created, so the two cases build different roots. Both
// already expose render, which is the shape the shared host expects.
export const mountView = createMountView(createElement, (container, element, shouldHydrate) => {
    if (shouldHydrate) {
        return hydrateRoot(container, element);
    }

    const root = createRoot(container);
    root.render(element);
    return root;
});

export { createElement };
