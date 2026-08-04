using JsxCore.Compilation.Modules;
namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed record RestoreResult(
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Actions,
    string? Failure = null)
{
    public bool Succeeded => Failure is null && Missing.Count == 0;
    public bool DidAnything => Actions.Count > 0;
}

public sealed class DependencyRestorer(IPackageManager packageManager, Action<string>? report = null)
{
    private readonly IPackageManager _packageManager =
        packageManager ?? throw new ArgumentNullException(nameof(packageManager));

    private readonly Action<string> _report = report ?? (_ => { });

    public RestoreResult Restore(string directory, IReadOnlyCollection<PackageRequest> required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(required);

        var actions = new List<string>();

        if (!File.Exists(Path.Combine(directory, "package.json")))
        {
            var created = _packageManager.CreateManifest(directory);
            if (!created.Succeeded)
            {
                return new RestoreResult(Absent(directory, required), actions, created.Failure);
            }
            actions.Add(created.Description);
        }

        if (Absent(directory, required) is { Count: 0 } && !AnythingDeclaredIsAbsent(directory))
        {
            return new RestoreResult([], actions);
        }

        // Reproducible first, and it leaves the working tree alone.
        // Reproducible first, and it leaves the working tree alone.
        if (File.Exists(Path.Combine(directory, "package-lock.json")))
        {
            var restored = _packageManager.RestoreFromLockFile(directory);
            actions.Add(restored.Succeeded ? restored.Description : $"lock file restore failed: {restored.Failure}");
        }

        // Anything JsxCore needs that the manifest does not declare has to be added to it.
        var undeclared = required.Where(package => !IsInstalled(directory, package.Name)).ToList();
        if (undeclared.Count > 0)
        {
            var added = _packageManager.Add(directory, undeclared);
            if (!added.Succeeded)
            {
                return new RestoreResult(Absent(directory, required), actions, added.Failure);
            }
            actions.Add(added.Description);
        }

        // Declared packages that the lock file could not satisfy, or that have no lock file yet.
        if (AnythingDeclaredIsAbsent(directory))
        {
            var installed = _packageManager.InstallDeclared(directory);
            if (!installed.Succeeded)
            {
                return new RestoreResult(Absent(directory, required), actions, installed.Failure);
            }
            actions.Add(installed.Description);
        }

        var stillMissing = Absent(directory, required)
            .Concat(DeclaredButAbsent(directory))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new RestoreResult(stillMissing, actions);
    }

    public static IReadOnlyList<string> DeclaredButAbsent(string directory) =>
        PackageManifest.In(directory) is { } manifest
            ? manifest.Packages.Where(package => !IsInstalled(directory, package.Name))
                .Select(package => package.Name).ToList()
            : [];

    private static bool AnythingDeclaredIsAbsent(string directory) => DeclaredButAbsent(directory).Count > 0;

    private static IReadOnlyList<string> Absent(string directory, IEnumerable<PackageRequest> required) =>
        required.Where(package => !IsInstalled(directory, package.Name))
            .Select(package => package.Name).ToList();

    private static bool IsInstalled(string directory, string name) =>
        File.Exists(Path.Combine(directory, "node_modules", name.Replace('/', Path.DirectorySeparatorChar), "package.json"));
}
