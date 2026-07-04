using System.Security.Cryptography;
using System.Text;

namespace JsxCore.Compilation.Assets;

public readonly record struct StagedFile(string RelativePath, byte[] Contents, bool Write = true);

public sealed record StageResult(string Fingerprint, bool Changed);

public interface IAssetSource
{
    IEnumerable<StagedFile> Enumerate();

    IEnumerable<string> Provenance => [];
}

public static class AssetStage
{
    public static StageResult WriteTo(string directory, IAssetSource source, string? prunePattern = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(source);

        Directory.CreateDirectory(directory);

        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var entry in source.Provenance)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry));
        }

        foreach (var file in source.Enumerate())
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData(file.Contents);

            // Counted towards the fingerprint without being written: every runtime module decides the
            // build id, but only its declarations are needed on disk.
            if (!file.Write)
            {
                continue;
            }

            var path = Path.Combine(directory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            written.Add(Path.GetFullPath(path));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            changed |= WriteFileIfChanged(path, file.Contents);
        }

        // Removing what a previous run left behind, so deleting a declaration does not leave a
        // stale one importable.
        if (prunePattern is not null)
        {
            foreach (var stale in Directory.EnumerateFiles(directory, prunePattern, SearchOption.AllDirectories)
                         .Where(path => !written.Contains(Path.GetFullPath(path)))
                         .ToList())
            {
                File.Delete(stale);
                changed = true;
            }

            RemoveEmptyDirectories(directory);
        }

        return new StageResult(Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant(), changed);
    }

    public static string Fingerprint(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var entry in source.Provenance)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry));
        }

        foreach (var file in source.Enumerate())
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData(file.Contents);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
    }

    public static bool WriteFileIfChanged(string path, byte[] contents)
    {
        if (File.Exists(path))
        {
            try
            {
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(contents))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                // A file being rewritten underneath us is overwritten rather than trusted.
            }
        }

        File.WriteAllBytes(path, contents);
        return true;
    }

    public static bool WriteFileIfChanged(string path, string contents) =>
        WriteFileIfChanged(path, Encoding.UTF8.GetBytes(contents));

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
