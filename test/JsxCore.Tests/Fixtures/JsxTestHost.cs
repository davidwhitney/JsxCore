using JsxCore.Hosting;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JsxCore.Tests.Fixtures;

/// <summary>
/// Runs a real ASP.NET Core pipeline over a <see cref="JsxProjectFixture"/> using the in-memory
/// test server, so middleware, routing, results and the WebSocket endpoint are all exercised the
/// way they would be in a hosted application.
/// </summary>
public sealed class JsxTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private JsxTestHost(WebApplication app, JsxProjectFixture project)
    {
        _app = app;
        Project = project;
    }

    /// <summary>The throwaway project this host serves.</summary>
    public JsxProjectFixture Project { get; }

    /// <summary>An HTTP client bound to the in-memory server.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>The underlying test server, for WebSocket clients and direct feature access.</summary>
    public TestServer Server => (TestServer)_app.Services.GetRequiredService<IServer>();

    /// <summary>
    /// Starts a host whose content root is the fixture's directory, which is what makes the view
    /// engine resolve views from the throwaway project rather than the test assembly's location.
    /// </summary>
    public static async Task<JsxTestHost> StartAsync(
        JsxProjectFixture project,
        Action<JsxCoreOptions>? configure = null,
        Action<WebApplication>? configureApp = null,
        string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = project.Root,
            EnvironmentName = environment,
            ApplicationName = typeof(JsxTestHost).Assembly.GetName().Name
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();

        builder.AddJsxCore(options =>
        {
            // The content root is a temporary directory with no node_modules above it, which is
            // exactly the situation an app with a relocated content root is in.
            options.TypeScriptCompilerPath = JsxProjectFixture.Toolchain.ExecutablePath;
            options.ViewsDirectory = project.Options.ViewsDirectory;
            options.WorkingDirectory = project.Options.WorkingDirectory;
            options.DefaultRenderMode = project.Options.DefaultRenderMode;
            options.TypeChecking = project.Options.TypeChecking;
            options.EnableReactCompatibility = project.Options.EnableReactCompatibility;

            foreach (var path in project.Options.AdditionalToolchainSearchPaths)
            {
                options.AdditionalToolchainSearchPaths.Add(path);
            }

            // The content root is a temp directory, so esbuild, which processes stylesheets, is
            // found where the rest of the toolchain is.
            options.AdditionalToolchainSearchPaths.Add(JsxProjectFixture.RepositoryRoot());

            options.WatchForChanges = project.Options.WatchForChanges;
            options.HotReload = project.Options.HotReload;

            foreach (var (name, registration) in project.Options.Globals.Registrations)
            {
                options.Globals.Register(name, registration.Factory);
            }

            configure?.Invoke(options);
        });

        var app = builder.Build();
        app.UseJsxCore();

        app.MapGet("/client/{view}", (string view) => Results.Extensions.Jsx($"Home/{view}", TestModel));
        app.MapGet("/server/{view}", (string view) => Results.Extensions.Jsx($"Home/{view}", TestModel, RenderMode.Server));
        app.MapGet("/hybrid/{view}", (string view) => Results.Extensions.Jsx($"Home/{view}", TestModel, RenderMode.ServerAndClient));

        configureApp?.Invoke(app);

        await app.StartAsync();

        var host = new JsxTestHost(app, project) { Client = app.GetTestClient() };
        return host;
    }

    private static object TestModel => new { name = "World", items = new[] { "alpha", "beta" } };

    /// <summary>Fetches a URL and returns the response body, asserting a successful status.</summary>
    public async Task<string> GetStringAsync(string url)
    {
        var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {url} returned {(int)response.StatusCode}:{Environment.NewLine}{body}");
        }

        return body;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
