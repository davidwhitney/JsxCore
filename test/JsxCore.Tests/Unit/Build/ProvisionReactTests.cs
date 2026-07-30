using JsxCore.Tool;
using Shouldly;

namespace JsxCore.Tests.Unit.Build;

[Trait("Category", "Network")]
public class ProvisionReactTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-provision-" + Guid.NewGuid().ToString("n")[..8]);

    public ProvisionReactTests() => Directory.CreateDirectory(Path.Combine(_root, "Views"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Provision_ReactFramework_InstallsTypesWhereTypeScriptLooksForThem()
    {
        var exit = ProvisionCommand.Run(Arguments.Parse([
            "--project-dir", _root,
            "--manifest-dir", _root + Path.DirectorySeparatorChar,
            "--framework", "react",
            "--auto-install", "true"
        ]));

        exit.ShouldBe(ProvisionCommand.Satisfied);

        File.Exists(Path.Combine(_root, "node_modules", "@types", "react", "index.d.ts"))
            .ShouldBeTrue("@types/react must unpack directly into its own directory");
    }
}
