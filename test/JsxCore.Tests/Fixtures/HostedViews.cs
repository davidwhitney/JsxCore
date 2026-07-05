using System.Text.RegularExpressions;

namespace JsxCore.Tests.Fixtures;

// A small view tree shared by the component tests: one page importing one shared component, which
// is enough to exercise rendering, asset serving and module resolution over HTTP.
public static partial class HostedViews
{
    public static JsxProjectFixture Project()
    {
        var project = JsxProjectFixture.Create();
        project.AddView("Shared/Card.tsx", """
            import type { JsxNode } from "@jsxcore/runtime";
            export function Card({ title, children }: { title: string; children?: JsxNode }) {
                return <section class="card"><h2>{title}</h2>{children}</section>;
            }
            """);
        project.AddView("Home/Index.tsx", """
            import { Card } from "../Shared/Card.tsx";
            import type { ViewProps } from "@jsxcore/runtime";
            export const head = { title: "Index page" };
            export default function Index({ model }: ViewProps<{ name: string; items: string[] }>) {
                return (
                    <Card title={`Hello ${model.name}`}>
                        <ul>{model.items.map((item) => <li key={item}>{item}</li>)}</ul>
                    </Card>
                );
            }
            """);
        return project;
    }

    [GeneratedRegex(@"/_jsx/v[0-9a-f]+/views/[^""]+\.js")]
    public static partial Regex ModuleUrl();

    [GeneratedRegex(@"/_jsx/v[0-9a-f]+")]
    public static partial Regex BuildPrefix();
}
