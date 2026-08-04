using JsxCore.Compilation.Modules;
namespace JsxCore.Compilation.Pipeline;

public sealed record ProjectInputs(NodeModulesLayout NodeModules, PackageManifest? Manifest)
{
    public IReadOnlySet<string> RuntimeDependencies =>
        Manifest?.RuntimeNames ?? new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Declared runtime dependencies that are not installed, so a view importing one would fail.
    /// </summary>
    /// <remarks>
    /// Runtime only. A devDependency that is absent is the normal state of a published application:
    /// the build carries dependencies into the output and deliberately leaves the compiler and the
    /// minifier behind, so warning about those would tell every deployment to go and build again.
    /// When one of them is missing anywhere it matters, the toolchain verifier says so first, and
    /// with the path it searched and the command that fixes it.
    /// </remarks>
    public IReadOnlyList<string> MissingPackages =>
        Manifest is null
            ? []
            : Manifest.Dependencies
                .Where(package => NodeModules.FindPackage(package.Name) is null)
                .Select(package => package.Name)
                .ToList();
}
