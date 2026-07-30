using System.Formats.Tar;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public static class PackageArchive
{
    public static async Task ExtractAsync(
        Stream tarball,
        string destination,
        string? integrity,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(tarball);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        // Buffered because the hash has to be computed over the whole archive before trusting it,
        // and the stream from the registry is forward only.
        using var buffer = new MemoryStream();
        await tarball.CopyToAsync(buffer, token).ConfigureAwait(false);
        buffer.Position = 0;

        if (!string.IsNullOrEmpty(integrity))
        {
            Verify(buffer, integrity);
            buffer.Position = 0;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(destination);

        await using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        while (await reader.GetNextEntryAsync(cancellationToken: token).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            if (RelativePathOf(entry.Name) is not { } relative)
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));

            // An archive must not be able to write outside the directory it is being unpacked into.
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.Ordinal))
            {
                throw new JsxCoreException($"The archive tried to write outside its directory: '{entry.Name}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using (var file = File.Create(path))
            {
                if (entry.DataStream is { } data)
                {
                    await data.CopyToAsync(file, token).ConfigureAwait(false);
                }
            }

            // The mode travels with the entry and has to be applied, or an executable arrives
            // without its executable bit. That is not cosmetic: the TypeScript compiler is shipped
            // as a binary in a package, and a compiler that cannot be run is a compiler that is
            // reported missing.
            if (!OperatingSystem.IsWindows() && entry.Mode != default)
            {
                File.SetUnixFileMode(path, entry.Mode);
            }
        }
    }

    public sealed record ArchiveManifest(
        string Name,
        string Version,
        IReadOnlyDictionary<string, string> Dependencies);

    // A repository archive has no packument behind it, so the manifest has to be read out of the
    // archive itself before anything is known about what it is or what it needs.
    public static async Task<ArchiveManifest> ReadManifestAsync(
        HttpClient http,
        string url,
        CancellationToken token = default)
    {
        using var response = await http.GetAsync(url, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
        buffer.Position = 0;

        await using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        while (await reader.GetNextEntryAsync(cancellationToken: token).ConfigureAwait(false) is { } entry)
        {
            if (RelativePathOf(entry.Name) != "package.json" || entry.DataStream is not { } data)
            {
                continue;
            }

            using var text = new StreamReader(data);
            using var document = JsonDocument.Parse(await text.ReadToEndAsync(token).ConfigureAwait(false));
            var root = document.RootElement;

            return new ArchiveManifest(
                root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                root.TryGetProperty("version", out var version) ? version.GetString() ?? "0.0.0" : "0.0.0",
                root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object
                    ? deps.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal));
        }

        throw new JsxCoreException($"The archive at '{url}' has no package.json in it.");
    }

    /// <summary>
    /// Where an entry belongs inside the package, or null when there is nothing left of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// npm tarballs put everything under a single root directory, conventionally "package/" but
    /// not always: DefinitelyTyped roots its archives at the package's own short name. npm strips
    /// whichever it is, so one leading component always goes.
    /// </para>
    /// <para>
    /// Components that cannot be a file name go first. .NET 8's TarReader joins the ustar prefix
    /// field onto the name for GNU-format archives, and GNU keeps access and change times in that
    /// field, so an entry arrives as "\0\0...\0 15232743476 15232743476/react/LICENSE": NUL padding,
    /// then two timestamps. That is not a path component, and no npm package has one containing a
    /// space or a control character, so discarding it costs nothing and stops the real root
    /// surviving into the extracted tree as a duplicate directory. .NET 10 reads the same archive
    /// correctly, which is why this only ever went wrong in the build tool.
    /// </para>
    /// </remarks>
    public static string? RelativePathOf(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
        {
            return null;
        }

        var segments = entryName.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(segment => segment.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            .Skip(1)
            .ToArray();

        return segments.Length == 0 ? null : string.Join('/', segments);
    }

    // Subresource integrity, as npm records it: "sha512-<base64>", possibly several space separated.
    public static void Verify(Stream archive, string integrity)
    {
        foreach (var candidate in integrity.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = candidate.IndexOf('-');
            if (dash < 0)
            {
                continue;
            }

            var algorithm = candidate[..dash];
            var expected = candidate[(dash + 1)..];

            archive.Position = 0;
            var actual = Convert.ToBase64String(algorithm switch
            {
                "sha512" => SHA512.HashData(archive),
                "sha256" => SHA256.HashData(archive),
                "sha1" => SHA1.HashData(archive),
                _ => []
            });

            if (actual.Length == 0)
            {
                continue;
            }

            if (actual != expected)
            {
                throw new JsxCoreException(
                    $"The downloaded archive does not match its published {algorithm} hash. " +
                    $"Expected {expected}, got {actual}.");
            }

            return;
        }

        throw new JsxCoreException($"No integrity hash in '{integrity}' could be checked.");
    }
}
