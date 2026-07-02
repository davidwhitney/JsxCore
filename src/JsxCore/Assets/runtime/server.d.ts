import type { JsxNode } from "./jsx-runtime.js";

/** Renders a node to an HTML string. Synchronous: server components must not be async. */
export declare function renderToString(node: JsxNode): string;

/** HTML-escapes a value for interpolation into markup. */
export declare function escapeHtml(value: unknown): string;

/** Invoked by the .NET host. Returns a JSON string of `{ html, head }`. */
export declare function renderView(viewModule: any, props: { model: unknown; context: unknown }): string;
