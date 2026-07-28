# Testing

← [Documentation index](README.md)

JsxCore works with `WebApplicationFactory` and `TestServer`. One thing to know up front:
**test hosts relocate the content root**, and JsxCore resolves views, the working directory
and the toolchain relative to it.

---

## WebApplicationFactory

`WebApplicationFactory<TEntryPoint>` sets the content root to the application project's directory,
which is what JsxCore needs, so it works with no extra configuration:

```csharp
public class HomeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HomeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Renders_the_home_page()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Hello", html);
    }
}
```

Your minimal API `Program.cs` needs a partial class for this, as usual:

```csharp
public partial class Program;
```

### Quietening it down

Turn off watching and hot reload so nothing recompiles behind your tests:

```csharp
public sealed class AppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Production");
}
```

`Production` disables both by default. If you need Development behaviour for some other reason,
set the options explicitly instead:

```csharp
options.WatchForChanges = false;
options.HotReload = false;
```

---

## TestServer directly

For a host you build yourself, set the content root explicitly:

```csharp
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = projectDirectory,
    EnvironmentName = "Development"
});

builder.WebHost.UseTestServer();
builder.AddJsxCore(options =>
{
    options.WatchForChanges = false;
    options.HotReload = false;
});

var app = builder.Build();
app.UseJsxCore();
app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", model));

await app.StartAsync();
var client = app.GetTestClient();
```

### When the content root has no node_modules

If your content root is a temporary directory, or otherwise sits outside the npm project, JsxCore
cannot find the compiler by walking upwards. Point it at the right place:

```csharp
options.AdditionalToolchainSearchPaths.Add(repositoryRoot);
// or
options.TypeScriptCompilerPath = locatedCompilerPath;
```

This is the single most likely reason a test fails with `JsxCoreEnvironmentException` while the
application runs fine.

---

## Testing hot reload

`TestServer` supports WebSockets, so the reload endpoint can be asserted on:

```csharp
var socketClient = server.CreateWebSocketClient();
using var socket = await socketClient.ConnectAsync(new Uri(server.BaseAddress, "/_jsx/hmr"), default);

// change a view on disk ...

var buffer = new byte[4096];
var received = await socket.ReceiveAsync(buffer, cancellationToken);
var message = Encoding.UTF8.GetString(buffer, 0, received.Count);

Assert.Contains("\"type\":\"update\"", message);
```

---

## What to assert on

| Mode | Reliable assertions |
|---|---|
| `Client` | The model JSON is present, the mount script references the right module, `<title>` from `head` |
| `Server` | The rendered markup itself, which is the interesting case |
| `ServerAndClient` | Both, plus `"hydrate":true` in the mount options |

Server-rendered HTML is the easiest thing to test, and it is real: the same compiled modules the
browser would load, executed by the same engine that serves production traffic.

```csharp
var html = await client.GetStringAsync("/report");

Assert.Contains("<h1>Quarterly report</h1>", html);
Assert.DoesNotContain("mountView", html);   // nothing shipped to the client
```

---

## Unit-testing a renderer directly

If you want to render a view without an HTTP pipeline, the pieces are public:

```csharp
var layout = CompilationLayout.Create(options, contentRoot);
var compilation = new JsxCompilationService(options, layout, toolchain, logger);
await compilation.InitialiseAsync();

var locator = new ViewLocator(options, layout, contentRoot);
var renderer = new JsxServerRenderer(options, compilation, JsxRuntimeLayout.Builtin());

var result = await renderer.RenderAsync(
    locator.Find("Home/Index", null, null, out _)!,
    model,
    new Dictionary<string, object?>(),
    services);

Assert.Equal("<h1>Hello World</h1>", result.Html);
```

This is how JsxCore's own test suite works. It compiles real TSX with the real compiler and renders
it through the real JavaScript engine. Neither is mocked, because the contract with tsc's emit is
the thing most worth pinning down.
