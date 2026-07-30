using JsxCore.Compilation.Modules;
using Shouldly;

namespace JsxCore.Tests.Unit.Modules;

public class PackageManifestSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jsxcore-manifest-" + Guid.NewGuid().ToString("n")[..8]);

    public PackageManifestSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Directories(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Manifest(string directory, string name) =>
        File.WriteAllText(Path.Combine(directory, "package.json"), $$"""{"name":"{{name}}"}""");

    [Fact]
    public void Nearest_ProjectHasAManifest_FindsIt()
    {
        var project = Directories("src", "Web");
        Manifest(project, "the-project");

        PackageManifest.Nearest(project).ShouldNotBeNull().Field("name").ShouldBe("the-project");
    }

    [Fact]
    public void Nearest_ProjectHasNone_DoesNotBorrowOneFromAboveIt()
    {
        // The reported failure: a new project with no package.json of its own, a few directories
        // below a home folder that happened to contain one from something unrelated. It was
        // adopted, and the build then restored that manifest's dependencies into that manifest's
        // node_modules, failing on what they declared. Nothing above the project is its business.
        Manifest(_root, "something-unrelated");

        PackageManifest.Nearest(Directories("dev", "temp", "ConsoleApp1", "WebApplication1")).ShouldBeNull();
    }

    [Fact]
    public void Nearest_ManifestIsOneDirectoryUp_IsStillNotUsed()
    {
        // Not even a near miss counts. A solution sharing one manifest says so with
        // JsxCoreManifestDirectory rather than being guessed at from proximity.
        Manifest(Directories("solution"), "the-solution");

        PackageManifest.Nearest(Directories("solution", "Web")).ShouldBeNull();
    }

    [Fact]
    public void Nearest_ManifestsExistAboveAndBeside_UsesTheOneBeside()
    {
        Manifest(Directories("solution"), "the-solution");
        var project = Directories("solution", "Web");
        Manifest(project, "the-project");

        PackageManifest.Nearest(project).ShouldNotBeNull().Field("name").ShouldBe("the-project");
    }

    [Fact]
    public void Nearest_DirectoryDoesNotExist_ReturnsNullRatherThanThrowing() =>
        PackageManifest.Nearest(Path.Combine(_root, "no", "such", "place")).ShouldBeNull();
}
