import { useState } from "react";
import type { ViewProps } from "@jsxcore/runtime";

export const head = { title: "JsxCore with React" };

export default function Index({ model }: ViewProps<{ framework: string; renderedAt: string }>) {
    const [count, setCount] = useState(0);

    return (
        <main>
            <h1>Hello from {model.framework}</h1>
            <p>Server rendered at {new Date(model.renderedAt).toISOString()}, then made interactive.</p>
            <button onClick={() => setCount(count + 1)}>Clicked {count} times</button>
        </main>
    );
}
