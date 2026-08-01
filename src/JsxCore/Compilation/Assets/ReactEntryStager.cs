using System.Text;

namespace JsxCore.Compilation.Assets;

/// <summary>
/// Writes JsxCore's React entry points into the working directory.
/// </summary>
/// <remarks>
/// React itself is not staged: it publishes CommonJS, so it goes through the same npm pipeline that
/// wraps and serves any other package. Only JsxCore's own entry points are written here.
/// </remarks>
public sealed class ReactEntryStager(CompilationLayout layout) : IAssetSource
{
    private readonly CompilationLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public string Directory => Path.Combine(_layout.WorkingDirectory, "react");

    public IEnumerable<StagedFile> Enumerate()
    {
        foreach (var shared in RuntimeAssets.SharedEntryFiles())
        {
            yield return shared;
        }

        foreach (var fileName in RuntimeAssets.ReactSourceFiles)
        {
            yield return new StagedFile(fileName, Encoding.UTF8.GetBytes(
                RuntimeAssets.TryGetReactSource(fileName)
                ?? throw new JsxCoreException($"Embedded React entry source '{fileName}' is missing.")));
        }
    }

    public string Stage() => AssetStage.WriteTo(Directory, this).Fingerprint;
}
