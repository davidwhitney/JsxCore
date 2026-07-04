namespace JsxCore.Compilation.Assets;

public sealed class RuntimeTypeDefinitions : IAssetSource
{
    public IEnumerable<StagedFile> Enumerate() => RuntimeAssets.AllFileNames.Select(fileName => new StagedFile(
        fileName,
        RuntimeAssets.TryGetContent(fileName)!,
        Write: fileName.EndsWith(".d.ts", StringComparison.Ordinal) || fileName == "package.json"));
}
