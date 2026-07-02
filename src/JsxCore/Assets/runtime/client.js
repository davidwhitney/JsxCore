// Client renderer for the built-in JsxCore runtime.
//
// This is a small keyed reconciler, not a general-purpose UI framework. It is enough to
// render a view, wire event handlers and support component-local state. Applications that
// want the full React programming model should switch JsxCore into React mode instead.
//
// The container element is assumed to be owned entirely by the root: nodes inside it that
// the renderer did not create may be moved or removed.

import { ELEMENT, Fragment } from "./jsx-runtime.js";
import {
    VOID_ELEMENTS,
    isReservedProp,
    isEventProp,
    eventNameFor,
    attributeNameFor,
    styleToString,
    classNameToString,
    isOmittedAttributeValue
} from "./dom.js";
import { dispatcher, depsChanged } from "./hooks.js";

const TEXT = 1;
const ELEMENT_NODE = 2;
const COMPONENT = 3;

let pendingEffects = [];

// ---------------------------------------------------------------------------
// Normalisation: turn arbitrary JSX children into a flat list of internal nodes.
// ---------------------------------------------------------------------------

function normalise(input, out) {
    if (input === null || input === undefined || input === false || input === true) {
        return;
    }
    if (Array.isArray(input)) {
        for (const item of input) {
            normalise(item, out);
        }
        return;
    }
    if (typeof input === "object" && input.$$typeof === ELEMENT) {
        if (input.type === Fragment) {
            normalise(input.props.children, out);
            return;
        }
        if (typeof input.type === "function") {
            out.push({ kind: COMPONENT, type: input.type, props: input.props, key: input.key, instance: null });
            return;
        }
        out.push({
            kind: ELEMENT_NODE,
            tag: input.type,
            props: input.props,
            key: input.key,
            children: null,
            dom: null
        });
        return;
    }
    out.push({ kind: TEXT, value: String(input), key: null, dom: null });
}

function normaliseChildren(children) {
    const out = [];
    normalise(children, out);
    return out;
}

function domsOf(node) {
    if (node.kind === COMPONENT) {
        const result = [];
        for (const child of node.instance.children) {
            for (const dom of domsOf(child)) {
                result.push(dom);
            }
        }
        return result;
    }
    return node.dom ? [node.dom] : [];
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

function applyProp(dom, name, value, previous) {
    if (isReservedProp(name)) {
        return;
    }

    if (isEventProp(name)) {
        const event = eventNameFor(name);
        if (previous) {
            dom.removeEventListener(event, previous);
        }
        if (typeof value === "function") {
            dom.addEventListener(event, value);
        }
        return;
    }

    if (name === "style") {
        dom.setAttribute("style", styleToString(value));
        return;
    }

    let resolved = value;
    if (name === "className" || name === "class") {
        resolved = classNameToString(value);
    }

    const attribute = attributeNameFor(name);

    // Form controls track state on the property rather than the attribute.
    if (attribute === "value" && "value" in dom) {
        dom.value = resolved === null || resolved === undefined ? "" : resolved;
        return;
    }
    if (attribute === "checked" && "checked" in dom) {
        dom.checked = !!resolved;
        return;
    }

    // Only null/undefined/false remove the attribute. An empty string is a real value:
    // alt="" is how an image is marked decorative, and the server renderer keeps it too.
    if (isOmittedAttributeValue(resolved)) {
        dom.removeAttribute(attribute);
        return;
    }
    if (resolved === true) {
        dom.setAttribute(attribute, "");
        return;
    }
    dom.setAttribute(attribute, String(resolved));
}

function applyProps(dom, props, oldProps) {
    const old = oldProps || {};
    for (const name of Object.keys(props)) {
        if (old[name] !== props[name] || isEventProp(name)) {
            applyProp(dom, name, props[name], isEventProp(name) ? old[name] : undefined);
        }
    }
    for (const name of Object.keys(old)) {
        if (!(name in props)) {
            if (isEventProp(name)) {
                dom.removeEventListener(eventNameFor(name), old[name]);
            } else if (!isReservedProp(name)) {
                dom.removeAttribute(attributeNameFor(name));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Component instances and hooks
// ---------------------------------------------------------------------------

function createInstance(node, parentDom) {
    return {
        node,
        parentDom,
        hooks: [],
        children: [],
        dirty: false,
        mounted: false
    };
}

function makeDispatcher(instance) {
    let index = 0;
    return {
        useState(initial) {
            const slot = index++;
            if (instance.hooks.length <= slot) {
                instance.hooks[slot] = {
                    value: typeof initial === "function" ? initial() : initial
                };
            }
            const hook = instance.hooks[slot];
            const setState = (next) => {
                const value = typeof next === "function" ? next(hook.value) : next;
                if (Object.is(value, hook.value)) {
                    return;
                }
                hook.value = value;
                scheduleUpdate(instance);
            };
            return [hook.value, setState];
        },
        useRef(initial) {
            const slot = index++;
            if (instance.hooks.length <= slot) {
                instance.hooks[slot] = { value: { current: initial } };
            }
            return instance.hooks[slot].value;
        },
        useEffect(effect, deps) {
            const slot = index++;
            const hook = instance.hooks[slot] || (instance.hooks[slot] = { deps: undefined, cleanup: undefined });
            if (hook.deps === undefined || depsChanged(hook.deps, deps)) {
                hook.deps = deps;
                pendingEffects.push(hook, effect);
            }
        },
        useMemo(factory, deps) {
            const slot = index++;
            const hook = instance.hooks[slot];
            if (!hook || depsChanged(hook.deps, deps)) {
                const value = factory();
                instance.hooks[slot] = { value, deps };
                return value;
            }
            return hook.value;
        }
    };
}

function renderInstance(instance) {
    const previous = dispatcher.current;
    dispatcher.current = makeDispatcher(instance);
    try {
        return normaliseChildren(instance.node.type(instance.node.props));
    } finally {
        dispatcher.current = previous;
    }
}

let updateQueue = new Set();
let flushScheduled = false;

function scheduleUpdate(instance) {
    instance.dirty = true;
    updateQueue.add(instance);
    if (flushScheduled) {
        return;
    }
    flushScheduled = true;
    queueMicrotask(flushUpdates);
}

function flushUpdates() {
    flushScheduled = false;
    const queue = updateQueue;
    updateQueue = new Set();
    for (const instance of queue) {
        if (!instance.dirty || !instance.mounted) {
            continue;
        }
        instance.dirty = false;
        const next = renderInstance(instance);
        instance.children = reconcile(instance.parentDom, instance.children, next);
    }
    runEffects();
}

function runEffects() {
    const effects = pendingEffects;
    pendingEffects = [];
    for (let i = 0; i < effects.length; i += 2) {
        const hook = effects[i];
        const effect = effects[i + 1];
        if (typeof hook.cleanup === "function") {
            hook.cleanup();
        }
        const cleanup = effect();
        hook.cleanup = typeof cleanup === "function" ? cleanup : undefined;
    }
}

// ---------------------------------------------------------------------------
// Mount / patch / unmount
// ---------------------------------------------------------------------------

function mount(node, parentDom) {
    if (node.kind === TEXT) {
        node.dom = document.createTextNode(node.value);
        return node;
    }

    if (node.kind === COMPONENT) {
        node.instance = createInstance(node, parentDom);
        node.instance.children = reconcile(parentDom, [], renderInstance(node.instance), true);
        node.instance.mounted = true;
        return node;
    }

    const dom = document.createElement(node.tag);
    node.dom = dom;
    applyProps(dom, node.props, null);

    if (node.props.dangerouslySetInnerHTML) {
        dom.innerHTML = node.props.dangerouslySetInnerHTML.__html || "";
        node.children = [];
        return node;
    }

    if (!VOID_ELEMENTS.has(node.tag)) {
        node.children = reconcile(dom, [], normaliseChildren(node.props.children), true);
    } else {
        node.children = [];
    }
    return node;
}

function unmount(node) {
    if (node.kind === COMPONENT) {
        for (const hook of node.instance.hooks) {
            if (hook && typeof hook.cleanup === "function") {
                hook.cleanup();
            }
        }
        node.instance.mounted = false;
        for (const child of node.instance.children) {
            unmount(child);
        }
        return;
    }
    if (node.children) {
        for (const child of node.children) {
            unmount(child);
        }
    }
    if (node.dom && node.dom.parentNode) {
        node.dom.parentNode.removeChild(node.dom);
    }
}

function sameType(a, b) {
    if (a.kind !== b.kind) {
        return false;
    }
    if (a.kind === ELEMENT_NODE) {
        return a.tag === b.tag;
    }
    if (a.kind === COMPONENT) {
        return a.type === b.type;
    }
    return true;
}

function patch(oldNode, newNode, parentDom) {
    if (newNode.kind === TEXT) {
        newNode.dom = oldNode.dom;
        if (oldNode.value !== newNode.value) {
            newNode.dom.nodeValue = newNode.value;
        }
        return newNode;
    }

    if (newNode.kind === COMPONENT) {
        const instance = oldNode.instance;
        instance.node = newNode;
        instance.parentDom = parentDom;
        newNode.instance = instance;
        instance.children = reconcile(parentDom, instance.children, renderInstance(instance));
        return newNode;
    }

    const dom = oldNode.dom;
    newNode.dom = dom;
    applyProps(dom, newNode.props, oldNode.props);

    if (newNode.props.dangerouslySetInnerHTML) {
        const html = newNode.props.dangerouslySetInnerHTML.__html || "";
        if (!oldNode.props.dangerouslySetInnerHTML || oldNode.props.dangerouslySetInnerHTML.__html !== html) {
            dom.innerHTML = html;
        }
        newNode.children = [];
        return newNode;
    }

    if (!VOID_ELEMENTS.has(newNode.tag)) {
        newNode.children = reconcile(dom, oldNode.children || [], normaliseChildren(newNode.props.children));
    } else {
        newNode.children = [];
    }
    return newNode;
}

// Reconciles a child list against a parent DOM node. `initial` skips the reordering pass
// for a freshly created parent, where appending in order is already correct.
function reconcile(parentDom, oldChildren, newChildren, initial) {
    const keyed = new Map();
    const unkeyed = [];
    for (const child of oldChildren) {
        if (child.key !== null && child.key !== undefined) {
            keyed.set(child.key, child);
        } else {
            unkeyed.push(child);
        }
    }

    const matched = new Set();
    const result = new Array(newChildren.length);
    let unkeyedCursor = 0;

    for (let i = 0; i < newChildren.length; i++) {
        const next = newChildren[i];
        let previous = null;

        if (next.key !== null && next.key !== undefined) {
            const candidate = keyed.get(next.key);
            if (candidate && sameType(candidate, next)) {
                previous = candidate;
            }
        } else {
            while (unkeyedCursor < unkeyed.length) {
                const candidate = unkeyed[unkeyedCursor++];
                if (sameType(candidate, next)) {
                    previous = candidate;
                    break;
                }
                // Types diverged; the old node is dropped below.
            }
        }

        if (previous) {
            matched.add(previous);
            result[i] = patch(previous, next, parentDom);
        } else {
            result[i] = mount(next, parentDom);
        }
    }

    for (const child of oldChildren) {
        if (!matched.has(child)) {
            unmount(child);
        }
    }

    // Place the resulting DOM nodes in order.
    const desired = [];
    for (const child of result) {
        for (const dom of domsOf(child)) {
            desired.push(dom);
        }
    }

    if (initial) {
        for (const dom of desired) {
            parentDom.appendChild(dom);
        }
    } else {
        for (let i = 0; i < desired.length; i++) {
            const dom = desired[i];
            if (parentDom.childNodes[i] !== dom) {
                parentDom.insertBefore(dom, parentDom.childNodes[i] || null);
            }
        }
    }

    return result;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export function createRoot(container) {
    let children = [];
    let first = true;
    return {
        render(element) {
            if (first) {
                // Server-rendered markup is replaced rather than hydrated in place.
                container.textContent = "";
                first = false;
            }
            children = reconcile(container, children, normaliseChildren(element), children.length === 0);
            runEffects();
        },
        unmount() {
            for (const child of children) {
                unmount(child);
            }
            children = [];
            container.textContent = "";
        }
    };
}

// Reads the model serialised by the .NET host and mounts the view component.
export function mountView(Component, options) {
    const settings = options || {};
    const containerId = settings.containerId || "jsxcore-root";
    const modelId = settings.modelId || "jsxcore-model";

    const container = document.getElementById(containerId);
    if (!container) {
        throw new Error("JsxCore: no container element with id '" + containerId + "' was found.");
    }

    const modelScript = document.getElementById(modelId);
    const model = modelScript && modelScript.textContent
        ? JSON.parse(modelScript.textContent)
        : null;

    const context = window.__jsxcore_context || {};
    const root = createRoot(container);
    root.render({ $$typeof: ELEMENT, type: Component, props: { model, context }, key: null });

    // Exposed so the hot reload client can swap the component without a full page load. It hands
    // over an update function rather than the renderer, because the element shape is the runtime's
    // business: the client used to build one itself, which produced a blank page under Preact.
    window.__jsxcore_root = {
        root,
        model,
        context,
        update: (next) => root.render({
            $$typeof: ELEMENT,
            type: next,
            props: { model, context },
            key: null
        })
    };

    return root;
}

export { Fragment };
