import { useState } from "preact/hooks";
import type { ViewProps } from "dotnet:rendering";
interface IndexModel { framework: string; count: number }

export const head = { title: "JsxCore with Tailwind" };

export default function Index({ model }: ViewProps<IndexModel>) {
    const [count, setCount] = useState(model.count);

    return (
        <main class="min-h-screen bg-slate-950 text-slate-100 flex items-center justify-center p-8">
            <div class="max-w-md w-full rounded-2xl bg-slate-900 ring-1 ring-slate-800 p-8 shadow-xl">
                <h1 class="text-3xl font-semibold tracking-tight">Hello from {model.framework}</h1>
                <p class="mt-2 text-slate-400">
                    Server rendered, then hydrated. The classes here were compiled from this file.
                </p>
                <button
                    class="mt-6 rounded-lg bg-indigo-500 px-4 py-2 font-medium hover:bg-indigo-400 transition"
                    onClick={() => setCount(count + 1)}>
                    Clicked {count} times
                </button>
            </div>
        </main>
    );
}
