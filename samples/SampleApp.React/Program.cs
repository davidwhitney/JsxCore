using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;
using JsxCore.TypeScript;

var builder = WebApplication.CreateBuilder(args);
builder.AddJsxCore(options => options.DefaultRenderMode = RenderMode.ServerAndClient);

var app = builder.Build();
app.UseJsxCore();
app.UseStaticFiles();

app.MapGet("/", () =>
{
    var model = new IndexModel("React", DateTimeOffset.Now);
    return Results.Extensions.Jsx("Home/Index", model);
});
app.Run();

[JsxModel]
public sealed record IndexModel(string Framework, DateTimeOffset RenderedAt);
