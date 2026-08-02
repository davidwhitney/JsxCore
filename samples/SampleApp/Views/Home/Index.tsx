import { useState } from "preact/hooks";
import type { ViewProps } from "dotnet:rendering";
import type SampleApp from "dotnet:types"; // Auto-exported TypeScript defs for C# classes
import { Page, Card } from "../Shared/Layout.tsx";

// The default export is the view component. It receives the .NET model plus an ambient context.
// This view uses the default RenderMode.Client, so the component runs in the browser and the
// server only emits a shell containing the serialised model.

export const head = (model: SampleApp.Models.IndexModel) => ({
    title: `JsxCore: ${model.greeting}`,
    meta: [{ name: "description", content: "A TSX view engine for ASP.NET Core" }]
});

export default function Index({ model }: ViewProps<SampleApp.Models.IndexModel>) {
    return (
        <Page title={model.greeting} active="/">
            <Card title="Client rendered">
                <p>
                    This component was compiled from TSX to ESM and mounted in the browser. The
                    model below arrived as JSON from ASP.NET Core.
                </p>
                <ul>
                    {model.features.map((feature) => (
                        <li key={feature}>{feature}</li>
                    ))}
                </ul>
                <p class="muted">Model generated at {new Date(model.generatedAt).toLocaleTimeString()}</p>
            </Card>

            <Card title="State works">
                <Counter />
            </Card>
        </Page>
    );
}

function Counter() {
    const [count, setCount] = useState(0);

    return (
        <div class="counter">
            <button type="button" onClick={() => setCount(count - 1)}>
                −
            </button>
            <output>{count}</output>
            <button type="button" onClick={() => setCount(count + 1)}>
                +
            </button>
            {count !== 0 && <p class="muted">Clicked to {count}. Server rendering never runs this.</p>}
        </div>
    );
}
