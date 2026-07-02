export { Fragment, ELEMENT, jsx, jsxs, createElement, isElement, JSX } from "./jsx-runtime.js";
export type { JsxNode, JsxElement, Component, CSSProperties, HTMLAttributes } from "./jsx-runtime.js";
export { useState, useRef, useEffect, useMemo, useCallback } from "./hooks.js";
export { dotnet, isServerRender } from "./dotnet.js";

/** Props every JsxCore view receives. */
export interface ViewProps<TModel = unknown> {
    /** The .NET model passed to the view, serialised to JSON. */
    model: TModel;
    /** Ambient request context supplied by the server. */
    context: ViewContext;
}

export interface ViewContext {
    path?: string;
    /** Values added with `options.Context.Add(...)` or per-request. */
    [key: string]: unknown;
}

/**
 * Optional named export on a view module, used to populate the document head when
 * JsxCore renders the surrounding HTML shell.
 */
export interface HeadDescriptor {
    title?: string;
    meta?: Array<Record<string, string>>;
    links?: Array<Record<string, string>>;
    scripts?: Array<Record<string, string>>;
}
