// The automatic JSX transform target for the built-in JsxCore runtime.
// esbuild is configured with jsxImportSource "@jsxcore/runtime", so every .tsx/.jsx
// file compiles down to calls into this module.

export const ELEMENT = Symbol.for("jsxcore.element");
export const Fragment = Symbol.for("jsxcore.fragment");

export function jsx(type, props, key) {
    return { $$typeof: ELEMENT, type, props: props || {}, key: key === undefined ? null : key };
}

// esbuild emits jsxs for static children lists; the shape is identical for us.
export const jsxs = jsx;

// Classic-runtime entry point, so `createElement`-style output also works.
export function createElement(type, props, ...children) {
    const merged = { ...(props || {}) };
    if (children.length === 1) {
        merged.children = children[0];
    } else if (children.length > 1) {
        merged.children = children;
    }
    return jsx(type, merged, merged.key === undefined ? null : merged.key);
}

export function isElement(value) {
    return !!value && typeof value === "object" && value.$$typeof === ELEMENT;
}
