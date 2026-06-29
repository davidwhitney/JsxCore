import type { ViewProps } from "@jsxcore/runtime";
import { marked } from "marked";
import { Page, Card } from "../Shared/Layout.tsx";

// `marked` is an ordinary npm package: npm install marked, then import it. It is resolved out of
// node_modules for server rendering, and served to the browser from the app for the client half of
// this page, with no bundler and no import map entry to write by hand.

export const head = { title: "JsxCore: npm packages" };

export default function Markdown({ model }: ViewProps<{ source: string }>) {
    return (
        <Page title="npm packages" active="/markdown">
            <Card title="Rendered by marked">
                <div dangerouslySetInnerHTML={{ __html: marked.parse(model.source) as string }} />
            </Card>

            <Card title="How it got here">
                <p>
                    This page renders on the server and again on the client. Both halves import the
                    same package: the server reads it from <code>node_modules</code>, and the browser
                    loads it from an import map entry JsxCore generated, pointing at the package
                    files it serves.
                </p>
                <p>
                    CommonJS packages work too, wrapped so they present a default export. Node
                    built-ins do not, because server rendering runs in an embedded engine.
                </p>
            </Card>
        </Page>
    );
}
