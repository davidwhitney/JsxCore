using Microsoft.AspNetCore.Mvc;

namespace SampleApp.Controllers;

/// <summary>
/// A conventional MVC controller. There is nothing JsxCore-specific here: because the view engine
/// is registered as an <c>IViewEngine</c>, <c>View(model)</c> resolves <c>Views/Home/Mvc.tsx</c>.
/// </summary>
public sealed class HomeController : Controller
{
    /// <summary>Renders Views/Home/Mvc.tsx.</summary>
    [HttpGet("/mvc")]
    public IActionResult Mvc() => View(new
    {
        heading = "Rendered through MVC",
        controller = "Home",
        action = "Mvc"
    });
}
