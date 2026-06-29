import { dotnet, isServerRender } from "@jsxcore/runtime";
import type { ViewProps } from "@jsxcore/runtime";
import { Page, Card } from "../Shared/Layout.tsx";

// Demonstrates calling .NET objects from a server-rendered view. Because the JavaScript engine
// runs in-process, these are direct CLR calls that return synchronously. There is no bridge to
// await and no serialisation round trip.

interface DashboardModel {
    heading: string;
}

// Shapes registered on the .NET side with options.Globals.Register(...).
interface Inventory {
    getTotalValue(): number;
    getLowStock(threshold: number): Array<{ sku: string; name: string; quantity: number }>;
}

interface Clock {
    now(): string;
    timeZone(): string;
}

export const head = { title: "JsxCore: .NET globals" };

export default function Dashboard({ model }: ViewProps<DashboardModel>) {
    if (!isServerRender()) {
        return (
            <Page title={model.heading} active="/dashboard">
                <Card title="Server only">
                    <p>This view reads .NET globals, so it has to run on the server.</p>
                </Card>
            </Page>
        );
    }

    const inventory = dotnet.Inventory as Inventory;
    const clock = dotnet.Clock as Clock;

    const lowStock = inventory.getLowStock(20);

    return (
        <Page title={model.heading} active="/dashboard">
            <Card title="Called straight into .NET">
                <p>
                    Total stock value is <strong>{inventory.getTotalValue().toFixed(2)}</strong>,
                    read from a C# service during rendering.
                </p>
                <p class="muted">
                    Server time {clock.now()} ({clock.timeZone()})
                </p>
            </Card>

            <Card title={`Low stock (${lowStock.length})`}>
                <ul>
                    {lowStock.map((item) => (
                        <li key={item.sku}>
                            <strong>{item.name}</strong>: {item.quantity} left
                            <span class="muted"> ({item.sku})</span>
                        </li>
                    ))}
                </ul>
            </Card>
        </Page>
    );
}
