namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed class PackageManagerSelector(IReadOnlyList<IPackageManager> managers)
{
    public IReadOnlyList<IPackageManager> Managers { get; } =
        managers ?? throw new ArgumentNullException(nameof(managers));

    public static PackageManagerSelector Default(JsxCoreOptions options, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The native client first, because it needs nothing installed and resolves the same tree
        // npm does. npm remains available for anything the native client does not cover, and for
        // anyone who would rather use the tool they already trust: name it to insist on it.
        return new PackageManagerSelector(
        [
            new Native.NativePackageManager(report: report),
            new NpmPackageManager(options.NpmPath, options.DependencyInstallTimeout, report)
        ]);
    }

    public IPackageManager? Select(string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var named = Managers.FirstOrDefault(
                manager => manager.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));

            return named?.IsAvailable() == true ? named : null;
        }

        // First that can run wins, so an installed npm keeps its current behaviour and a native
        // implementation added after it is reached only where npm is absent.
        return Managers.FirstOrDefault(manager => manager.IsAvailable());
    }

    public string DescribeAll() => string.Join(", ", Managers.Select(manager => manager.Name));
}
