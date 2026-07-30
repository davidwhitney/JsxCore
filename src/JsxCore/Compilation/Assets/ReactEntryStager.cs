using System.Text;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// Writes JsxCore's React entry points into the working directory.
/// </summary>
/// <remarks>
/// Far less to do than the Preact equivalent, because React itself is not staged. React publishes
/// CommonJS, which a browser cannot load, so it goes through the same npm pipeline that serves any
/// other package: resolved out of node_modules, wrapped as a module, and served with the rest. Only
/// these two files, which are JsxCore's own, are written here.
/// </remarks>
public sealed class ReactEntryStager(CompilationLayout layout) : IAssetSource
{
    private readonly CompilationLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public string Directory => Path.Combine(_layout.WorkingDirectory, "react");

    public IEnumerable<StagedFile> Enumerate()
    {
        foreach (var fileName in RuntimeAssets.ReactSourceFiles)
        {
            yield return new StagedFile(fileName, Encoding.UTF8.GetBytes(
                RuntimeAssets.TryGetReactSource(fileName)
                ?? throw new JsxCoreException($"Embedded React entry source '{fileName}' is missing.")));
        }
    }

    public string Stage() => AssetStage.WriteTo(Directory, this).Fingerprint;
}
