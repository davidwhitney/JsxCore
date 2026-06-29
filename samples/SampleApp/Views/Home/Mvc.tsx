import type { ViewProps } from "@jsxcore/runtime";
import { Page, Card } from "../Shared/Layout.tsx";

// Reached through a plain MVC controller returning View(model). JsxCore registers itself as an
// IViewEngine, so "Views/Home/Mvc.tsx" is found exactly the way a .cshtml file would be.

interface MvcModel {
    heading: string;
    controller: string;
    action: string;
}

export const head = { title: "JsxCore: MVC controller" };

export default function Mvc({ model, context }: ViewProps<MvcModel>) {
    return (
        <Page title={model.heading} active="/mvc">
            <Card title="return View(model)">
                <p>
                    No JsxCore-specific code in the controller: the view engine resolved
                    <code> Views/{model.controller}/{model.action}.tsx</code> through the normal MVC
                    view location logic.
                </p>
                <dl>
                    <dt>Controller</dt>
                    <dd>{model.controller}</dd>
                    <dt>Action</dt>
                    <dd>{model.action}</dd>
                    <dt>Request path</dt>
                    <dd>{String(context.path)}</dd>
                </dl>
            </Card>
        </Page>
    );
}
