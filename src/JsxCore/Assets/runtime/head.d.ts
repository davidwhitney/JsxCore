/**
 * Sets document head tags from inside a component, the way `next/head` does.
 *
 * ```tsx
 * import Head from "dotnet:rendering/head";
 *
 * export default function Products({ model }: ViewProps<ProductsModel>) {
 *     return (
 *         <>
 *             <Head>
 *                 <title>{model.category}</title>
 *                 <meta name="description" content={model.summary} />
 *             </Head>
 *             <h1>{model.category}</h1>
 *         </>
 *     );
 * }
 * ```
 *
 * Renders nothing where it sits. `title`, `meta`, `link` and `script` children are lifted into the
 * document head; anything else is ignored.
 *
 * A server render writes them into the HTML. A client render applies them to `document.head`, and
 * reconciles against what a server render already wrote, so a hydrated page does not end up with
 * two of everything.
 *
 * A `head` export is the other way to do this, and the two combine: the component wins on the
 * title, and tags from both are emitted. Unlike this component, a `head` export is read without
 * running the view, so a client-rendered page gets its title in the first response rather than
 * after mounting.
 */
export default function Head(props: {
    /**
     * `title`, `meta`, `link` and `script` elements. Typed loosely because the elements are the
     * active framework's, and this component reads them structurally rather than as one of them.
     */
    children?: unknown;
}): null;

export { Head };
