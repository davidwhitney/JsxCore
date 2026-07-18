using System.Text;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;

namespace JsxCore.TypeScript;

public sealed record GeneratedTypeScriptFile(string RelativePath, string ModuleSpecifier, string Contents);

public sealed class GeneratedTypeScript(IReadOnlyList<GeneratedTypeScriptFile> files) : IAssetSource
{
    public IReadOnlyList<GeneratedTypeScriptFile> Files { get; } = files ?? throw new ArgumentNullException(nameof(files));

    public IEnumerable<StagedFile> Enumerate() => Files.Select(
        file => new StagedFile(file.RelativePath, Encoding.UTF8.GetBytes(file.Contents)));

    /// <summary>
    /// Writes the declarations, removing any left by a previous run so that deleting a .NET
    /// namespace does not leave a stale module importable.
    /// </summary>
    public StageResult WriteTo(string directory) => AssetStage.WriteTo(directory, this, prunePattern: "*.d.ts");
}
