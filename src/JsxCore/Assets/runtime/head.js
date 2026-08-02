// <Head>, for setting document head tags from inside a component, the way next/head does.
//
//     import Head from "dotnet:rendering/head";
//
//     <Head>
//         <title>Products</title>
//         <meta name="description" content="Everything we sell" />
//     </Head>
//
// It renders nothing where it sits. Its children are lifted into the document head instead: by the
// .NET host during a server render, and by this module directly during a client one.
//
// Framework agnostic on purpose. Preact and React vnodes both carry `type` and `props`, which is
// all this reads, so there is one copy rather than one per framework.

// Marks the elements this owns, so a client render can reconcile against what a server render
// already wrote instead of appending a second copy of everything.
const MARKER = "data-jsxcore-head";

const TAGS = { title: "title", meta: "meta", link: "link", script: "script" };

/** The text inside a <title>, flattening fragments, arrays and interpolated values. */
function textOf(children) {
    if (children === null || children === undefined || children === false || children === true) {
        return "";
    }

    if (Array.isArray(children)) {
        return children.map(textOf).join("");
    }

    if (typeof children === "object") {
        return textOf(children.props ? children.props.children : undefined);
    }

    return String(children);
}

/** Attributes of a vnode, as the flat string bag a HeadDescriptor carries. */
function attributesOf(node) {
    const attributes = {};

    for (const name in node.props) {
        if (name === "children" || name === "key" || name === "ref") {
            continue;
        }

        const value = node.props[name];
        if (value === null || value === undefined || value === false) {
            continue;
        }

        attributes[name] = value === true ? "" : String(value);
    }

    return attributes;
}

/**
 * A key identifying one emitted element. Computed the same way on both sides, so an element the
 * server wrote is recognised by the client rather than duplicated.
 */
function keyOf(tag, attributes) {
    const parts = [tag];
    for (const name in attributes) {
        if (name !== MARKER) {
            parts.push(name + "=" + attributes[name]);
        }
    }

    return parts.join("|");
}

/** Walks children, turning the ones this understands into head entries. */
function collect(children, into) {
    if (children === null || children === undefined || children === false || children === true) {
        return into;
    }

    if (Array.isArray(children)) {
        for (const child of children) {
            collect(child, into);
        }
        return into;
    }

    if (typeof children !== "object" || !children.props) {
        return into;
    }

    const tag = TAGS[children.type];
    if (!tag) {
        // A fragment, or a component wrapping more tags. Look inside it rather than at it.
        collect(children.props.children, into);
        return into;
    }

    if (tag === "title") {
        into.push({ tag, title: textOf(children.props.children) });
        return into;
    }

    const attributes = attributesOf(children);
    attributes[MARKER] = keyOf(tag, attributes);
    into.push({ tag, attributes });

    return into;
}

/**
 * Reconciles the document head against what this render wants, in the browser.
 */
function applyToDocument(entries) {
    const wanted = new Map();

    for (const entry of entries) {
        if (entry.tag === "title") {
            document.title = entry.title;
            continue;
        }

        wanted.set(entry.attributes[MARKER], entry);
    }

    // Anything previously written that this render no longer asks for.
    for (const existing of document.head.querySelectorAll("[" + MARKER + "]")) {
        const key = existing.getAttribute(MARKER);
        if (wanted.has(key)) {
            wanted.delete(key);
        } else {
            existing.remove();
        }
    }

    for (const entry of wanted.values()) {
        const element = document.createElement(entry.tag);
        for (const name in entry.attributes) {
            element.setAttribute(name, entry.attributes[name]);
        }
        document.head.appendChild(element);
    }
}

/**
 * Sets document head tags from inside a component.
 */
export default function Head(props) {
    const entries = collect(props ? props.children : null, []);

    // A server render leaves an array here for the duration of the pass, which is how this knows
    // which side it is on without asking the framework or the host.
    const collecting = globalThis.__jsxcore_head;
    if (Array.isArray(collecting)) {
        for (const entry of entries) {
            collecting.push(entry);
        }

        return null;
    }

    // Applied while rendering rather than from an effect, so this needs no hooks and works the
    // same under Preact and React. Reconciling by key makes it safe to run repeatedly, which is
    // what hydration and every re-render do.
    if (typeof document !== "undefined") {
        applyToDocument(entries);
    }

    return null;
}

export { Head };
