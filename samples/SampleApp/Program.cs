using JsxCore;
using JsxCore.Hosting;
using JsxCore.Mvc;
using SampleApp.Models;
using SampleApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<ClockService>();

// Registering the view engine verifies the environment immediately: if the TypeScript toolchain
// is missing or too old, startup fails here with a message explaining how to install it.
builder.AddJsxCore(options =>
{
    options.DefaultRenderMode = RenderMode.Client;

    // Published output already contains the compiled views, so production needs no toolchain.
    options.PrecompiledOnly = builder.Environment.IsProduction();

    // Expose .NET objects to server-rendered views.
    options.Globals.Register<InventoryService>("Inventory");
    options.Globals.Register<ClockService>("Clock");

    // Nothing to configure for view model types: everything in a "Models" namespace is exported
    // to TypeScript by convention. options.AutoExport takes over when that is not what you want.
    // options.AutoExport = TypesFrom.AllUserCode;

    options.Document.DefaultTitle = "JsxCore sample";
    options.Document.HeadContent = "<link rel=\"stylesheet\" href=\"/site.css\">";
});

var app = builder.Build();

app.UseJsxCore();
app.UseStaticFiles();

app.MapGet("/", () => Results.Extensions.Jsx("Home/Index", new IndexModel(
    Greeting: "Hello from ASP.NET Core",
    Features:
    [
        "TSX and JSX views compiled by the native TypeScript compiler",
        "Native ESM: components import each other with no bundler",
        "Client, server, or both",
        "Direct calls into .NET during server rendering",
        "View model types generated from C#",
        "Hot reload in development"
    ],
    GeneratedAt: DateTimeOffset.Now)));

app.MapGet("/markdown", () => Results.Extensions.JsxServerAndClient("Home/Markdown", new
{
    source = "# Hello\n\nRendered by **marked**, an npm package, on the server *and* the client."
}));

app.MapGet("/server", () => Results.Extensions.JsxServerRendered("Home/Server", new TeamModel(
    Heading: "Rendered on the server",
    Rows:
    [
        new TeamMember("Ada Lovelace", "Analyst", new DateOnly(1843, 10, 1)),
        new TeamMember("Grace Hopper", "Compiler engineer", new DateOnly(1952, 4, 11)),
        new TeamMember("Barbara Liskov", "Language designer", new DateOnly(1974, 9, 30))
    ])));

app.MapGet("/hybrid", () => Results.Extensions.JsxServerAndClient("Home/Hybrid", new
{
    heading = "Server rendered, then interactive",
    items = new[]
    {
        "Compilation", "Reconciliation", "Hydration", "Serialisation",
        "Routing", "Caching", "Diagnostics", "Hot reload"
    }
}));

app.MapGet("/preact", () => Results.Extensions.JsxServerAndClient("Home/Preact", new CatalogueModel(
    Heading: "Rendered with Preact",
    Products:
    [
        new Product(1, "Mechanical keyboard", 89.00m, Availability.InStock) { StockKeepingUnit = "SKU-1001" },
        new Product(2, "27-inch monitor", 249.50m, Availability.Backordered) { StockKeepingUnit = "SKU-1002" },
        new Product(3, "USB-C dock", 129.99m, Availability.InStock) { StockKeepingUnit = "SKU-1003" },
        new Product(4, "Desk mat", 24.00m, Availability.Discontinued) { StockKeepingUnit = "SKU-1004" }
    ])
    { Featured = new Product(1, "Mechanical keyboard", 89.00m, Availability.InStock) }));

app.MapGet("/dashboard", () => Results.Extensions.JsxServerRendered("Home/Dashboard", new
{
    heading = "Reading .NET from a view"
}));

app.MapControllers();

app.Run();

/// <summary>Exposed so the integration tests can host this application.</summary>
public partial class Program;
