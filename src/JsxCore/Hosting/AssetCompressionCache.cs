using System.Collections.Concurrent;
using System.IO.Compression;

namespace JsxCore.Hosting;

/// <summary>How an asset is encoded on the wire.</summary>
public enum AssetEncoding
{
    Identity,
    Gzip,
    Brotli
}

/// <summary>
/// Compresses assets once per build and holds the result.
/// </summary>
/// <remarks>
/// Asset URLs are build-id scoped and immutable, so a compressed body computed once stays correct
/// until the build changes, and compressing the same module on every request would be pure waste.
/// The cache is dropped wholesale when the build id moves, which bounds it to one build's payload.
/// <para>
/// This is the one place where doing the work at request time rather than at build time is the
/// simpler answer: assets arrive from three sources — disk, the assembly manifest, and the npm
/// graph held in memory — and compressing at the point they are served covers all three without
/// each having to grow its own output.
/// </para>
/// </remarks>
public sealed class AssetCompressionCache
{
    private readonly ConcurrentDictionary<(string BuildId, string Name, AssetEncoding Encoding), byte[]> _entries = new();
    private string? _buildId;

    /// <summary>Encodings this cache will produce, best first.</summary>
    public static AssetEncoding Negotiate(string? acceptEncoding)
    {
        if (string.IsNullOrEmpty(acceptEncoding))
        {
            return AssetEncoding.Identity;
        }

        // Brotli first: it is smaller than gzip on this kind of content and every browser that
        // sends it also sends gzip, so preferring it costs nothing in reach.
        if (Accepts(acceptEncoding, "br"))
        {
            return AssetEncoding.Brotli;
        }

        return Accepts(acceptEncoding, "gzip") ? AssetEncoding.Gzip : AssetEncoding.Identity;
    }

    /// <summary>
    /// The compressed body for an asset, computed on first request for this build.
    /// </summary>
    public byte[] Get(string buildId, string name, AssetEncoding encoding, Func<byte[]> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A new build invalidates everything: the same name now means different bytes.
        if (!string.Equals(_buildId, buildId, StringComparison.Ordinal))
        {
            _entries.Clear();
            _buildId = buildId;
        }

        return _entries.GetOrAdd((buildId, name, encoding), _ => Compress(content(), encoding));
    }

    public static string HeaderValueFor(AssetEncoding encoding) => encoding switch
    {
        AssetEncoding.Brotli => "br",
        AssetEncoding.Gzip => "gzip",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Identity has no encoding header.")
    };

    /// <summary>
    /// Compression that is worth paying for once, since the result is cached for the whole build:
    /// the slowest settings, rather than the ones tuned for per-response compression.
    /// </summary>
    private static byte[] Compress(byte[] content, AssetEncoding encoding)
    {
        using var output = new MemoryStream();

        switch (encoding)
        {
            case AssetEncoding.Brotli:
                using (var stream = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    stream.Write(content);
                }

                break;

            case AssetEncoding.Gzip:
                using (var stream = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    stream.Write(content);
                }

                break;

            default:
                return content;
        }

        return output.ToArray();
    }

    private static bool Accepts(string header, string encoding)
    {
        foreach (var part in header.Split(','))
        {
            var candidate = part.Trim();
            var semicolon = candidate.IndexOf(';');
            var name = (semicolon >= 0 ? candidate[..semicolon] : candidate).Trim();

            if (!name.Equals(encoding, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (semicolon < 0)
            {
                return true;
            }

            // "gzip;q=0" is a refusal, and is the one qualifier worth honouring. "q=0.5" is not
            // one: it expresses a preference between encodings, and we have already chosen.
            var qualifier = candidate[(semicolon + 1)..].Replace(" ", "");
            return !qualifier.StartsWith("q=0", StringComparison.OrdinalIgnoreCase)
                   || qualifier.StartsWith("q=0.", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
