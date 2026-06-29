import { useState, useContext, createContext, useMemo } from "preact/compat";
import type { SampleApp } from "@jsxcore/generated";
import { Page, Card } from "../Shared/Layout.tsx";

// Rendered with real Preact. Context, the full hook set and error boundaries all work the way
// they would in React, and preact/compat means React-targeted components do too.

const CurrencyContext = createContext("£");

export const head = { title: "JsxCore: Preact" };

function Price({ amount }: { amount: number }) {
    // Context is one of the things the built-in runtime cannot do.
    const symbol = useContext(CurrencyContext);
    return <strong>{symbol}{amount.toFixed(2)}</strong>;
}

export default function PreactView({ model }: { model: SampleApp.Models.CatalogueModel; context: unknown }) {
    const [query, setQuery] = useState("");

    const visible = useMemo(
        () => model.products.filter((p) => p.name.toLowerCase().includes(query.toLowerCase())),
        [model.products, query]
    );

    const total = useMemo(() => visible.reduce((sum, p) => sum + p.price, 0), [visible]);

    // Same simple name, different namespace; the generated modules keep them distinct.
    const listing: SampleApp.Models.Catalogue.Product = { code: "CAT-1", description: "Catalogue entry", availability: "InStock" };

    return (
        <CurrencyContext.Provider value="£">
            <Page title={model.heading} active="/preact">
                <Card title="Real Preact, real hooks">
                    <p>
                        This view is rendered by Preact: server-rendered for first paint, then
                        hydrated in place rather than replaced. Context, useMemo and the rest of the
                        hook set all behave as you would expect.
                    </p>
                    <input
                        type="search"
                        placeholder="Filter products..."
                        value={query}
                        onInput={(e: any) => setQuery(e.currentTarget.value)}
                    />
                    <ul>
                        {visible.map((product) => (
                            <li key={product.id}>
                                {product.name}: <Price amount={product.price} />
                                <span class="muted"> {product.sku} · {product.availability}</span>
                            </li>
                        ))}
                    </ul>
                    <p class="muted">
                        {visible.length} shown, total <Price amount={total} />
                    </p>
                    <p class="muted">
                        Catalogue: {listing.code}, {listing.description} ({listing.availability})
                    </p>
                </Card>
            </Page>
        </CurrencyContext.Provider>
    );
}
