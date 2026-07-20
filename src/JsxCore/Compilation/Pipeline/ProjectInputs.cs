using JsxCore.Compilation.Modules;
namespace JsxCore.Compilation.Pipeline;

public sealed record ProjectInputs(NodeModulesLayout NodeModules, PackageManifest? Manifest)
{
    public IReadOnlySet<string> RuntimeDependencies =>
        Manifest?.RuntimeNames ?? new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<string> MissingPackages =>
        Manifest is null
            ? []
            : Manifest.Packages
                .Where(package => NodeModules.FindPackage(package.Name) is null)
                .Select(package => package.Name)
                .ToList();
}
