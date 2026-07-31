using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.AddJsxCore(options => options.DefaultRenderMode = RenderMode.ServerAndClient);

var app = builder.Build();
app.UseJsxCore();
app.UseStaticFiles();   // serves wwwroot/favicon.ico

app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new IndexModel("React", DateTimeOffset.Now)));
app.Run();

public sealed record IndexModel(string Framework, DateTimeOffset RenderedAt);
