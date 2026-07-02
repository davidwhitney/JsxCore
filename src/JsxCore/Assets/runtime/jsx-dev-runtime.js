// Development build of the automatic JSX transform. esbuild targets this module
// when `jsx: "automatic"` runs in development mode; it passes extra debug arguments
// which we deliberately ignore beyond attaching them for error messages.

import { jsx, Fragment, ELEMENT, createElement, isElement } from "./jsx-runtime.js";

export { Fragment, ELEMENT, createElement, isElement };

export function jsxDEV(type, props, key, _isStaticChildren, source) {
    const element = jsx(type, props, key);
    if (source) {
        element.__source = source;
    }
    return element;
}

export { jsxDEV as jsx, jsxDEV as jsxs };
