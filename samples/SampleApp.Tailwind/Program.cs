using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.AddJsxCore(options =>
{
    options.DefaultRenderMode = RenderMode.ServerAndClient;

    // The stylesheet Tailwind compiled, served from wwwroot like any other static file.
    options.Document.HeadContent = """<link rel="stylesheet" href="/app.css">""";
});

var app = builder.Build();

app.UseJsxCore();
app.UseStaticFiles();

app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new IndexModel("Tailwind", 3)));

app.Run();

public sealed record IndexModel(string Framework, int Count);
