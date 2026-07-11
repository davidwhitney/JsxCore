using System.Net;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using Shouldly;

using JsxCore.Tests.Fixtures;

namespace JsxCore.Tests.Component.Mvc;

public class ViewResolutionTests
{
    [Fact]
    public async Task ViewLookup_ViewDoesNotExist_ReportsEveryLocationSearched()
    {
        using var project = HostedViews.Project();
        await using var host = await JsxTestHost.StartAsync(project);

        // The developer exception page turns the failure into a 500 whose body carries the detail.
        var response = await host.Client.GetAsync("/client/DoesNotExist");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain(nameof(JsxViewNotFoundException));
        body.ShouldContain("DoesNotExist");
        body.ShouldContain("Locations searched");
    }
}
