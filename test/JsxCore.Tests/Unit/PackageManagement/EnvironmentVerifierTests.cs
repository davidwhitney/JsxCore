using JsxCore.Compilation;
using JsxCore.Rendering;
using Shouldly;

using JsxCore.Tests.Fixtures;
using JsxCore.Compilation.Provisioning;

namespace JsxCore.Tests.Unit.PackageManagement;

public class EnvironmentVerifierTests
{
    [Fact]
    public void Verify_CompilerIsMissing_ExplainsHowToInstallIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-env", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Views"));
        var options = new JsxCoreOptions { TypeScriptCompilerPath = "/definitely/not/a/compiler" };

        try
        {
            var exception = Should.Throw<JsxCoreEnvironmentException>(() => EnvironmentVerifier.Verify(options, root));

            exception.Message.ShouldContain("npm install --save-dev typescript@^7");
            exception.Message.ShouldContain("/definitely/not/a/compiler");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Verify_ViewsDirectoryDoesNotExist_WarnsRatherThanFailing()
    {
        // A view can be named by absolute path, which resolves without the views directory being
        // involved at all. An application doing only that should not have to create a directory it
        // never reads.
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-env", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(root);
        var options = new JsxCoreOptions { TypeScriptCompilerPath = JsxProjectFixture.Toolchain.ExecutablePath };

        try
        {
            var warnings = new List<string>();

            EnvironmentVerifier.Verify(options, root, bootstrapFailure: null, warn: warnings.Add)
                .ShouldNotBeNull();

            warnings.ShouldHaveSingleItem().ShouldContain("ViewsDirectory");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Verify_CompilerIsBelowTheMinimumVersion_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-env", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Views"));
        var options = new JsxCoreOptions
        {
            TypeScriptCompilerPath = JsxProjectFixture.Toolchain.ExecutablePath,
            MinimumTypeScriptMajorVersion = 999
        };

        try
        {
            var exception = Should.Throw<JsxCoreEnvironmentException>(() => EnvironmentVerifier.Verify(options, root));
            exception.Message.ShouldContain("requires TypeScript 999");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Verify_EnvironmentIsValid_SucceedsAndCreatesTheWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "jsxcore-env", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Views"));
        var options = new JsxCoreOptions { TypeScriptCompilerPath = JsxProjectFixture.Toolchain.ExecutablePath };

        try
        {
            EnvironmentVerifier.Verify(options, root).ShouldNotBeNull().MajorVersion.ShouldBeGreaterThanOrEqualTo(7);
            Directory.Exists(Path.Combine(root, "obj", "JsxCore")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
