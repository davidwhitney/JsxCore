import "./page.css";
import { useState } from "react";
import { Button, Card } from "sample-ui";
import type { ViewProps } from "dotnet:rendering";
import type { IndexModel } from "dotnet:types";

export const head = { title: "JsxCore server rendering" };

export default function Index({ model }: ViewProps<IndexModel>) {
    const [count, setCount] = useState(model.count);

    return (
        <main>
            <Card title={`Rendered with ${model.renderMode}`}>
                <p>
                    Server rendered at {new Date(model.renderedAt).toISOString()}. The card and buttons come from
                    <code> sample-ui</code>, a component library that imports its own CSS; the document links that
                    CSS, so the page is styled before, and without, any JavaScript.
                </p>
                {/* Without JavaScript the form round-trips to the server; once hydrated, the click is handled here. */}
                <form className="actions" method="get">
                    <Button type="submit" name="count" value={count + 1}
                        onClick={(event) => { event.preventDefault(); setCount(count + 1); }}>
                        Clicked {count} times
                    </Button>
                    <a href={model.renderMode === "Server" ? "/" : "/server-only"}>
                        {model.renderMode === "Server" ? "Hydrated version" : "No-JavaScript version"}
                    </a>
                </form>
            </Card>
        </main>
    );
}
