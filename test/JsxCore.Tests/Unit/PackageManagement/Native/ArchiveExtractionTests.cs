using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

[Trait("Category", "Network")]
public class ArchiveExtractionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-mode-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Extract_PackageContainsAnExecutable_KeepsItExecutable()
    {
        // The TypeScript compiler is a binary inside a package. Losing the executable bit on the
        // way out of the archive means it installs and then cannot be run, which is reported as a
        // missing compiler rather than as a broken one.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        new NativePackageManager(report: _ => { })
            .Add(_root, [new PackageRequest("typescript", "^7.0.0", Development: true)])
            .Succeeded.ShouldBeTrue();

        var compilers = Directory.EnumerateFiles(_root, "tsc", SearchOption.AllDirectories).ToList();
        compilers.ShouldNotBeEmpty();

        foreach (var compiler in compilers)
        {
            File.GetUnixFileMode(compiler).HasFlag(UnixFileMode.UserExecute)
                .ShouldBeTrue($"{compiler} should be executable");
        }
    }
}
