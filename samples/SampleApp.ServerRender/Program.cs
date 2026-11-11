using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;
using JsxCore.TypeScript;

var builder = WebApplication.CreateBuilder(args);
builder.AddJsxCore(options => options.DefaultRenderMode = RenderMode.ServerAndClient);

var app = builder.Build();
app.UseJsxCore();
app.UseStaticFiles();

// Server rendered, then hydrated: the page is complete before any script runs.
app.MapGet("/", (int count = 0) =>
    Results.Extensions.Jsx("Home/Index", new IndexModel("ServerAndClient", DateTimeOffset.Now, count)));

// The same view with no JavaScript at all: markup and the library's stylesheets, nothing else.
// Its button still works: it submits a plain form, and the server renders the next count.
app.MapGet("/server-only", (int count = 0) => Results.Extensions.Jsx(
    "Home/Index", new IndexModel("Server", DateTimeOffset.Now, count), RenderMode.Server));

app.Run();

[JsxModel]
public sealed record IndexModel(string RenderMode, DateTimeOffset RenderedAt, int Count);
