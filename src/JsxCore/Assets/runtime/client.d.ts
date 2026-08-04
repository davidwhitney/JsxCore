// The type surface of `@jsxcore/client`: mounting a view into the page.
//
// The module behind it is not in this directory. `@jsxcore/client` resolves to whichever entry
// point matches the framework the project builds against, so a project that switches between
// Preact and React keeps working without editing an import.
//
// Only `mountView` is declared, and deliberately so. The two entry points do not have the same
// surface -- Preact's also exports `render` and `hydrate` -- and a declaration promising exports
// that exist under one framework and not the other is the shape of trap this codebase has removed
// once already. Anything framework-specific is imported from the framework, by name.

/** Options controlling how a view is attached to the document. */
export interface MountOptions {
    /** Element id of the container the view renders into. */
    containerId: string;
    /** Element id of the script tag holding the serialised model. */
    modelId: string;
    /**
     * Whether to adopt server-rendered markup rather than replace it. True for a view that was
     * rendered on the server for first paint and is being made interactive.
     */
    hydrate?: boolean;
}

/**
 * Mounts a view component into the page, reading its model from the document.
 *
 * JsxCore writes this call itself in the document it generates. It is public for an application
 * that replaces that document with its own template, and needs to emit the same call.
 */
export declare function mountView(component: unknown, options: MountOptions): void;
