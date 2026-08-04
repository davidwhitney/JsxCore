// The type surface of `dotnet:rendering`: which pass is running, what a view is handed, and what
// it may contribute to the document.
//
// Hooks, the JSX factory and the renderers are not here. They belong to Preact or React, are
// imported from those packages by name, and are typed by their own declarations.

export { isServerRender } from "./dotnet.js";

/**
 * Anything a component may return.
 *
 * Declared here rather than taken from the framework so that a shared component can type its
 * `children` without naming Preact or React, and keep compiling if the project switches.
 */
export type JsxNode =
    | string
    | number
    | bigint
    | boolean
    | null
    | undefined
    | object
    | JsxNode[];

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
