// Client entry for Preact. The framework-specific half of mounting a view; the rest is shared.

import { createElement, render, hydrate } from "preact";
import { createMountView } from "./view-host.js";

// Preact decides hydration per render rather than when a root is created, so both cases render
// into the same container and the "root" is just a closure over it.
export const mountView = createMountView(createElement, (container, element, shouldHydrate) => {
    (shouldHydrate ? hydrate : render)(element, container);
    return { render: (next) => render(next, container) };
});

export { createElement, render, hydrate };
