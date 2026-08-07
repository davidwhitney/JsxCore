using JsxCore.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>Verifies the antiforgery pattern documented in docs/dotnet-interop.md renders.</summary>
public class RazorHelperPatternTests
{
    public sealed class AntiforgeryTokens
    {
        public string FieldName() => "__RequestVerificationToken";
        public string Token() => "CfDJ8-probe";
    }

    [Fact]
    public async Task DocumentedAntiforgeryPattern_Renders()
    {
        using var project = JsxProjectFixture.Create();
        project.Options.TypeChecking = TypeCheckingMode.Off;
        project.Options.Globals.Register<AntiforgeryTokens>("Antiforgery");

        // Exactly the view from the docs, as a page so it renders on its own.
        project.AddView("Home/Form.tsx", """
            import { Antiforgery } from "dotnet:globals";
            export default function Form() {
                return <input type="hidden" name={Antiforgery.fieldName()} value={Antiforgery.token()} />;
            }
            """);
        await project.CompileAsync();

        // Register<T> resolves per render from the request scope, so the service must be in DI.
        var services = new ServiceCollection()
            .AddScoped<AntiforgeryTokens>()
            .BuildServiceProvider();

        var result = await project.RenderAsync("Home/Form", services: services);

        result.Html.ShouldBe(
            """<input type="hidden" name="__RequestVerificationToken" value="CfDJ8-probe"/>""");
    }
}
