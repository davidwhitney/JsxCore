using System.Text;

namespace JsxCore.Compilation.Assets;

public sealed class VendoredPreactModules(PreactVendorStager stager) : IAssetSource
{
    private readonly PreactVendorStager _stager = stager ?? throw new ArgumentNullException(nameof(stager));

    // Installed versions are in the fingerprint as well as the contents, so an upgrade moves the
    // build id even where the copied bytes happen not to change.
    public IEnumerable<string> Provenance => PreactVendorStager.VersionedPackages.Select(
        package => $"{package}@{_stager.ReadInstalledVersion(package) ?? VendoredPreact.Versions.GetValueOrDefault(package, "unknown")}");

    public IEnumerable<StagedFile> Enumerate()
    {
        foreach (var module in PreactVendorStager.Modules)
        {
            // An installed copy wins, so upgrading Preact is an npm install rather than a wait for
            // the next JsxCore release. Otherwise the copy shipped in the package is used, which is
            // what lets a project render without installing anything at all.
            if (_stager.ResolveInNodeModules(module.PackagePath) is { } source)
            {
                yield return new StagedFile(module.FileName, File.ReadAllBytes(source));
                continue;
            }

            if (VendoredPreact.ReadModule(module.FileName) is { } vendored)
            {
                yield return new StagedFile(module.FileName, vendored);
                continue;
            }

            if (module.Required)
            {
                throw new JsxCoreEnvironmentException(_stager.MissingModuleMessage(module));
            }

            _stager.LogOptionalModuleMissing(module);
        }

        foreach (var shared in RuntimeAssets.SharedEntryFiles())
        {
            yield return shared;
        }

        // JsxCore's own mount and render entry points sit alongside them and import bare "preact".
        foreach (var fileName in RuntimeAssets.PreactSourceFiles)
        {
            yield return new StagedFile(fileName, Encoding.UTF8.GetBytes(
                RuntimeAssets.TryGetPreactSource(fileName)
                ?? throw new JsxCoreException($"Embedded Preact entry source '{fileName}' is missing.")));
        }
    }
}
