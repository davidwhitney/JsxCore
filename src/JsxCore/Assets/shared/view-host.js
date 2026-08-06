// Everything a framework entry point does that is not actually about the framework.
//
// Preact and React differ in about six lines: which module createElement comes from, how a root is
// attached to a container, and what turns an element into a string. Everything around that: finding
// the container, parsing the model out of the page, the shape of window.__jsxcore_root, the head
// export, refusing an async component, is the contract JsxCore has with a view, and is the same
// whatever renders it. It lives here so there is one copy to change when that contract changes.
//
// Staged into each framework's own directory rather than served from one place, so the entries can
// import it relatively and neither the import map nor the module loader needs to know it exists.

const DEFAULT_CONTAINER_ID = "jsxcore-root";
const DEFAULT_MODEL_ID = "jsxcore-model";

/**
 * The page as a view needs it: where to render, what model to render, and in what context.
 */
export function readPage(options) {
    const settings = options || {};
    const containerId = settings.containerId || DEFAULT_CONTAINER_ID;
    const modelId = settings.modelId || DEFAULT_MODEL_ID;

    const container = document.getElementById(containerId);
    if (!container) {
        throw new Error("JsxCore: no container element with id '" + containerId + "' was found.");
    }

    const modelScript = document.getElementById(modelId);

    return {
        container,
        model: modelScript && modelScript.textContent ? JSON.parse(modelScript.textContent) : null,
        context: window.__jsxcore_context || {},
        hydrate: Boolean(settings.hydrate)
    };
}

/**
 * Builds a framework's mountView.
 *
 * @param createElement the framework's element factory.
 * @param attach (container, element, hydrate) => an object with a render(element) method. Preact
 *        renders into a container and decides hydration per render; React builds a root that
 *        decided it already. Returning a common shape is what lets everything below be shared.
 */
export function createMountView(createElement, attach) {
    return function mountView(Component, options) {
        const page = readPage(options);
        const props = { model: page.model, context: page.context };
        const root = attach(page.container, createElement(Component, props), page.hydrate);

        // Published for the hot reload client, which re-imports a view and asks for it to be
        // rendered again. It builds no elements itself, so it needs to know nothing about which
        // framework is mounted.
        window.__jsxcore_root = {
            model: page.model,
            context: page.context,
            createElement,
            root,
            update: (next) => root.render(createElement(next, props))
        };

        return window.__jsxcore_root;
    };
}

function resolveHead(viewModule, props) {
    const head = typeof viewModule.head === "function"
        ? viewModule.head(props.model, props.context)
        : (viewModule.head || null);

    return head || null;
}

/**
 * Renders with a sink in place for <Head> to push into, and returns what it collected alongside
 * the markup.
 *
 * The sink is a global rather than something threaded through the tree, because <Head> can be
 * anywhere in it and neither Preact nor React offers a way to reach a render-scoped value without
 * a context provider the view would have to opt into. Restored afterwards so a nested render
 * cannot strand it.
 */
function renderCollectingHead(render) {
    const outer = globalThis.__jsxcore_head;
    globalThis.__jsxcore_head = [];

    try {
        return { html: render(), contributed: globalThis.__jsxcore_head };
    } finally {
        globalThis.__jsxcore_head = outer;
    }
}

/**
 * Folds what <Head> contributed into the descriptor the head export produced.
 *
 * The component wins on title, because it ran with everything the view knew by then; tags are
 * appended, because two components each adding a meta tag both meant it.
 */
function mergeHead(head, contributed) {
    if (!contributed || contributed.length === 0) {
        return head;
    }

    const merged = {
        title: head && head.title,
        meta: (head && head.meta) ? head.meta.slice() : [],
        links: (head && head.links) ? head.links.slice() : [],
        scripts: (head && head.scripts) ? head.scripts.slice() : []
    };

    for (const entry of contributed) {
        if (entry.tag === "title") {
            merged.title = entry.title;
        } else if (entry.tag === "meta") {
            merged.meta.push(entry.attributes);
        } else if (entry.tag === "link") {
            merged.links.push(entry.attributes);
        } else if (entry.tag === "script") {
            merged.scripts.push(entry.attributes);
        }
    }

    return merged;
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

/**
 * Builds a framework's server entry: renderView and readHead, which the .NET host calls and reads
 * the result of. The contract is identical for every framework, which is the point.
 *
 * Both return { html, head }: the markup as the string it already is, and the head descriptor as
 * JSON, or null when the view contributed no head at all. A whole page of markup is by far the
 * larger of the two and gains nothing from being escaped and unescaped on the way out; the head
 * descriptor is a handful of tags with a shape the host already knows how to read.
 */
export function createServerEntry(createElement, renderToString) {
    return {
        renderView(viewModule, props) {
            const Component = viewModule.default;
            if (typeof Component !== "function") {
                throw new Error("JsxCore: a view must have a default export that is a component function.");
            }

            const element = createElement(
                synchronous(Component), { model: props.model, context: props.context });

            const rendered = renderCollectingHead(() => renderToString(element));
            const head = mergeHead(resolveHead(viewModule, props), rendered.contributed);

            return { html: rendered.html, head: head ? JSON.stringify(head) : null };
        },

        // Only the head export, because the component is not run in this pass. A <Head> inside a
        // client-rendered view is applied by the browser after it mounts.
        readHead(viewModule, props) {
            const head = resolveHead(viewModule, props);

            return { html: "", head: head ? JSON.stringify(head) : null };
        }
    };
}
