using System.Net;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.HotReload;

public class HotReloadTests
{
    [Fact]
    public async Task HotReloadClient_OutsideDevelopment_IsNotInjected()
    {
        // Leaving both settings null is what makes AddJsxCore fall back to the environment.
        static void UseEnvironmentDefaults(JsxCoreOptions options)
        {
            options.HotReload = null;
            options.WatchForChanges = null;
        }

        using var development = HostedViews.Project();
        await using var developmentHost = await JsxTestHost.StartAsync(
            development, configure: UseEnvironmentDefaults, environment: "Development");
        (await developmentHost.GetStringAsync("/client/Index")).ShouldContain("hmr-client.js");

        using var production = HostedViews.Project();
        await using var productionHost = await JsxTestHost.StartAsync(
            production, configure: UseEnvironmentDefaults, environment: "Production");
        (await productionHost.GetStringAsync("/client/Index")).ShouldNotContain("hmr-client.js");
    }

    [Fact]
    public async Task HotReloadEndpoint_ViewChanges_PushesTheNewBuildIdOverTheSocket()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, o =>
        {
            o.HotReload = true;
            o.WatchForChanges = true;
        });

        var webSocketClient = host.Server.CreateWebSocketClient();
        var uri = new Uri(host.Server.BaseAddress, "/_jsx/hmr");
        using var socket = await webSocketClient.ConnectAsync(uri, CancellationToken.None);

        // Change a view; the watcher recompiles and the service broadcasts the new build id.
        project.AddView("Home/Index.tsx", """
            import { Card } from "../Shared/Card.tsx";
            import type { ViewProps } from "dotnet:rendering";
            export default function Index({ model }: ViewProps<{ name: string }>) {
                return <Card title={`Changed ${model.name}`} />;
            }
            """);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var buffer = new byte[4096];
        var received = await socket.ReceiveAsync(buffer, timeout.Token);
        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count);

        message.ShouldContain("\"type\":\"update\"");
        message.ShouldContain("\"version\"");
    }

    [Fact]
    public async Task HotReloadEndpoint_RequestIsNotAWebSocket_IsRejected()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project, o => o.HotReload = true);

        var response = await host.Client.GetAsync("/_jsx/hmr");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
