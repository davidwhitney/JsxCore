import { useState } from "preact/hooks";
import { isServerRender } from "dotnet:rendering";
import type { ViewProps } from "dotnet:rendering";
import { Page, Card } from "../Shared/Layout.tsx";

// Returned with RenderMode.ServerAndClient: the markup is produced on the server for first paint,
// then the same component is mounted in the browser so the interactive parts come alive.

interface HybridModel {
    heading: string;
    items: string[];
}

export const head = { title: "JsxCore: server and client" };

export default function Hybrid({ model }: ViewProps<HybridModel>) {
    const [filter, setFilter] = useState("");

    const visible = filter
        ? model.items.filter((item) => item.toLowerCase().includes(filter.toLowerCase()))
        : model.items;

    return (
        <Page title={model.heading} active="/hybrid">
            <Card title="Rendered twice, deliberately">
                <p>
                    The first paint came from the server, so this content is present for crawlers
                    and works without JavaScript. The filter below is wired up once the component
                    mounts in the browser.
                </p>
                <p class="muted">
                    This pass is running on the {isServerRender() ? "server" : "client"}.
                </p>

                <input
                    type="search"
                    placeholder="Filter..."
                    value={filter}
                    onInput={(event: any) => setFilter(event.target.value)}
                />

                <ul>
                    {visible.map((item) => (
                        <li key={item}>{item}</li>
                    ))}
                </ul>
                {visible.length === 0 && <p class="muted">Nothing matches “{filter}”.</p>}
            </Card>
        </Page>
    );
}
