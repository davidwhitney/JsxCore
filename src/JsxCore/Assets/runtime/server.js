// Server-side renderer. This module is evaluated inside Jint, so it is deliberately
// synchronous throughout: .NET globals are real CLR calls that return immediately,
// which means server components never need to await anything.

import { ELEMENT, Fragment, jsx } from "./jsx-runtime.js";
import {
    VOID_ELEMENTS,
    RAW_TEXT_ELEMENTS,
    isReservedProp,
    isEventProp,
    attributeNameFor,
    styleToString,
    classNameToString,
    isOmittedAttributeValue
} from "./dom.js";
import { dispatcher, serverDispatcher } from "./hooks.js";

const ESCAPE_PATTERN = /[&<>"']/g;
const ESCAPE_REPLACEMENTS = {
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
};

export function escapeHtml(value) {
    const text = String(value);
    // Fast path: most strings contain nothing that needs escaping.
    ESCAPE_PATTERN.lastIndex = 0;
    if (!ESCAPE_PATTERN.test(text)) {
        return text;
    }
    return text.replace(ESCAPE_PATTERN, (ch) => ESCAPE_REPLACEMENTS[ch]);
}

function isPromiseLike(value) {
    return !!value && typeof value.then === "function";
}

function renderChildren(children, out) {
    if (Array.isArray(children)) {
        for (const child of children) {
            renderNode(child, out);
        }
        return;
    }
    renderNode(children, out);
}

function renderAttributes(props, out) {
    for (const name of Object.keys(props)) {
        if (isReservedProp(name)) {
            continue;
        }
        // Event handlers have no meaning in static markup; the client renderer wires them up.
        if (isEventProp(name)) {
            continue;
        }

        let value = props[name];
        if (name === "style") {
            value = styleToString(value);
            if (!value) {
                continue;
            }
        } else if (name === "className" || name === "class") {
            value = classNameToString(value);
            if (!value) {
                continue;
            }
        }

        if (isOmittedAttributeValue(value)) {
            continue;
        }

        const attribute = attributeNameFor(name);
        if (value === true) {
            out.push(" ", attribute);
            continue;
        }
        out.push(" ", attribute, '="', escapeHtml(value), '"');
    }
}

function renderNode(node, out) {
    if (node === null || node === undefined || node === false || node === true) {
        return;
    }

    if (typeof node === "string") {
        out.push(escapeHtml(node));
        return;
    }

    if (typeof node === "number" || typeof node === "bigint") {
        out.push(String(node));
        return;
    }

    if (Array.isArray(node)) {
        for (const child of node) {
            renderNode(child, out);
        }
        return;
    }

    if (isPromiseLike(node)) {
        throw new Error(
            "JsxCore: server rendering is synchronous, but a component returned a Promise. " +
            "Calls to .NET globals return synchronously, so async components are not needed."
        );
    }

    if (typeof node !== "object" || node.$$typeof !== ELEMENT) {
        out.push(escapeHtml(node));
        return;
    }

    const { type, props } = node;

    if (type === Fragment) {
        renderChildren(props.children, out);
        return;
    }

    if (typeof type === "function") {
        renderNode(type(props), out);
        return;
    }

    if (typeof type !== "string") {
        throw new Error("JsxCore: unsupported element type '" + String(type) + "'.");
    }

    const tag = type;
    out.push("<", tag);
    renderAttributes(props, out);

    if (VOID_ELEMENTS.has(tag)) {
        out.push(" />");
        return;
    }

    out.push(">");

    const raw = props.dangerouslySetInnerHTML;
    if (raw && typeof raw.__html === "string") {
        out.push(raw.__html);
    } else if (RAW_TEXT_ELEMENTS.has(tag)) {
        // <script>/<style> content must not be HTML-escaped.
        const children = props.children;
        out.push(typeof children === "string" ? children : String(children ?? ""));
    } else {
        renderChildren(props.children, out);
    }

    out.push("</", tag, ">");
}

export function renderToString(node) {
    const previous = dispatcher.current;
    dispatcher.current = serverDispatcher;
    try {
        const out = [];
        renderNode(node, out);
        return out.join("");
    } finally {
        dispatcher.current = previous;
    }
}

function resolveHead(viewModule, props) {
    const head = typeof viewModule.head === "function"
        ? viewModule.head(props.model, props.context)
        : (viewModule.head || null);
    return head || null;
}

// Entry point invoked by the .NET host. Returns JSON so that a single string crosses
// the Jint interop boundary rather than a structured object graph.
export function renderView(viewModule, props) {
    const Component = viewModule.default;
    if (typeof Component !== "function") {
        throw new Error("JsxCore: a view must have a default export that is a component function.");
    }

    return JSON.stringify({
        html: renderToString(jsx(Component, props)),
        head: resolveHead(viewModule, props)
    });
}

// Used for client-rendered views, where the component itself never runs on the server but its
// head export still has to populate the document.
export function readHead(viewModule, props) {
    return JSON.stringify({ html: "", head: resolveHead(viewModule, props) });
}
