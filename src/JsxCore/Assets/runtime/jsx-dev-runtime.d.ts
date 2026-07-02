export { Fragment, ELEMENT, createElement, isElement, JSX } from "./jsx-runtime.js";
export type { JsxNode, JsxElement, Component, CSSProperties, HTMLAttributes } from "./jsx-runtime.js";

import type { JsxElement } from "./jsx-runtime.js";

export declare function jsxDEV(
    type: unknown,
    props: Record<string, any>,
    key?: string | number | null,
    isStaticChildren?: boolean,
    source?: unknown
): JsxElement;

export { jsxDEV as jsx, jsxDEV as jsxs };
