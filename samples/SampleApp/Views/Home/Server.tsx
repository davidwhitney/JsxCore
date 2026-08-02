// This page is markup and nothing else, so it says where it runs and the endpoint does not have to.
"use server";

import type { ViewProps } from "dotnet:rendering";
import type SampleApp from "dotnet:SampleApp";
import { Page, Card } from "../Shared/Layout.tsx";

// Returned with RenderMode.Server, so this component executes on the server and the response is
// plain HTML. No JavaScript is sent to the browser at all.

export const head = { title: "JsxCore: server rendered" };

export default function Server({ model }: ViewProps<SampleApp.Models.TeamModel>) {
    return (
        <Page title={model.heading} active="/server">
            <Card title="Traditional view engine">
                <p>
                    The component ran on the server and the response is the finished markup; view
                    source to confirm. In a production build no JavaScript is sent for this page at
                    all; in development you will also see the hot reload client.
                </p>
                <table>
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th>Role</th>
                            <th>Joined</th>
                        </tr>
                    </thead>
                    <tbody>
                        {model.rows.map((row) => (
                            <tr key={row.name}>
                                <td>{row.name}</td>
                                <td>{row.role}</td>
                                <td>{row.joined}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </Card>
        </Page>
    );
}
