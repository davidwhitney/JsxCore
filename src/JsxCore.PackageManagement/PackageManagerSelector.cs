namespace JsxCore.Compilation.Provisioning.PackageManagement;

public sealed class PackageManagerSelector(IReadOnlyList<IPackageManager> managers)
{
    public IReadOnlyList<IPackageManager> Managers { get; } =
        managers ?? throw new ArgumentNullException(nameof(managers));

    /// <param name="npmPath">Explicit path to npm, or null to probe for it.</param>
    /// <param name="npmTimeout">How long a single npm command may run for.</param>
    /// <remarks>
    /// Takes the two values it needs rather than the options object they usually come from: this
    /// assembly is the npm client on its own, and the view engine's configuration type is not
    /// something a package manager should have to know about to be constructed.
    /// </remarks>
    public static PackageManagerSelector Default(
        string? npmPath, TimeSpan npmTimeout, Action<string>? report = null)
    {
        // The native client first, because it needs nothing installed and resolves the same tree
        // npm does. npm remains available for anything the native client does not cover, and for
        // anyone who would rather use the tool they already trust: name it to insist on it.
        return new PackageManagerSelector(
        [
            new Native.NativePackageManager(report: report),
            new NpmPackageManager(npmPath, npmTimeout, report)
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

        // First that can run wins, which is the native client: it is always available, so npm is
        // reached only by being named.
        return Managers.FirstOrDefault(manager => manager.IsAvailable());
    }

    public string DescribeAll() => string.Join(", ", Managers.Select(manager => manager.Name));
}
