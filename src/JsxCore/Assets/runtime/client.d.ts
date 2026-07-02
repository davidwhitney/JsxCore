import type { JsxNode, Component } from "./jsx-runtime.js";

export interface Root {
    render(element: JsxNode): void;
    unmount(): void;
}

/** Creates a render root. The container's children are owned entirely by the root. */
export declare function createRoot(container: Element): Root;

export interface MountOptions {
    /** Element id holding the rendered output. Defaults to "jsxcore-root". */
    containerId?: string;
    /** Element id of the JSON script tag holding the model. Defaults to "jsxcore-model". */
    modelId?: string;
}

/** Reads the serialised model from the page and mounts the view component. */
export declare function mountView(component: Component<any>, options?: MountOptions): Root;

export { Fragment } from "./jsx-runtime.js";
