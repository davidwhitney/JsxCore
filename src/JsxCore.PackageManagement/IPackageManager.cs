namespace JsxCore.Compilation.Provisioning.PackageManagement;

public interface IPackageManager
{
    string Name { get; }

    bool IsAvailable();

    PackageOperationResult CreateManifest(string directory);

    PackageOperationResult RestoreFromLockFile(string directory);

    PackageOperationResult InstallDeclared(string directory);

    PackageOperationResult Add(string directory, IReadOnlyCollection<PackageRequest> packages);
}
