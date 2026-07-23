using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

public sealed record RegistryPackage(
    string Name,
    SemanticVersion Version,
    string TarballUrl,
    string Integrity,
    IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyDictionary<string, string> OptionalDependencies,
    IReadOnlyList<string> OperatingSystems,
    IReadOnlyList<string> Architectures,
    IReadOnlyDictionary<string, string> Engines,
    IReadOnlyDictionary<string, string> PeerDependencies,
    IReadOnlySet<string> OptionalPeers)
{
    // npm ships one package per platform for things like the TypeScript compiler, and marks them
    // with os and cpu. Installing the wrong one wastes a download; installing none breaks the build.
    public bool RunsHere() =>
        Matches(OperatingSystems, PlatformName()) && Matches(Architectures, ArchitectureName());

    private static bool Matches(IReadOnlyList<string> allowed, string actual)
    {
        if (allowed.Count == 0)
        {
            return true;
        }

        var negations = allowed.Where(a => a.StartsWith('!')).Select(a => a[1..]).ToList();
        if (negations.Count > 0)
        {
            return !negations.Contains(actual, StringComparer.OrdinalIgnoreCase);
        }

        return allowed.Contains(actual, StringComparer.OrdinalIgnoreCase);
    }

    public static string PlatformName() =>
        OperatingSystem.IsWindows() ? "win32"
        : OperatingSystem.IsMacOS() ? "darwin"
        : OperatingSystem.IsLinux() ? "linux"
        : "unknown";

    public static string ArchitectureName() =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "ia32",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            var other => other.ToString().ToLowerInvariant()
        };
}

public sealed class NpmRegistry(HttpClient http, string registryUrl = "https://registry.npmjs.org")
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly string _registry = registryUrl.TrimEnd('/');
    private readonly Dictionary<string, JsonDocument> _packuments = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<SemanticVersion>> VersionsAsync(string name, CancellationToken token = default)
    {
        var packument = await PackumentAsync(name, token).ConfigureAwait(false);
        if (!packument.RootElement.TryGetProperty("versions", out var versions))
        {
            return [];
        }

        return versions.EnumerateObject()
            .Select(v => SemanticVersion.TryParse(v.Name, out var parsed) ? parsed : null)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();
    }

    public async Task<RegistryPackage?> DescribeAsync(string name, SemanticVersion version, CancellationToken token = default)
    {
        var packument = await PackumentAsync(name, token).ConfigureAwait(false);
        if (!packument.RootElement.TryGetProperty("versions", out var versions)
            || !versions.TryGetProperty(version.ToString(), out var entry))
        {
            return null;
        }

        var dist = entry.TryGetProperty("dist", out var d) ? d : default;

        return new RegistryPackage(
            name,
            version,
            dist.ValueKind == JsonValueKind.Object && dist.TryGetProperty("tarball", out var t) ? t.GetString() ?? "" : "",
            dist.ValueKind == JsonValueKind.Object && dist.TryGetProperty("integrity", out var i) ? i.GetString() ?? "" : "",
            ReadMap(entry, "dependencies"),
            ReadMap(entry, "optionalDependencies"),
            ReadList(entry, "os"),
            ReadList(entry, "cpu"),
            ReadMap(entry, "engines"),
            ReadMap(entry, "peerDependencies"),
            OptionalPeerNames(entry));
    }

    public async Task<Stream> DownloadAsync(RegistryPackage package, CancellationToken token = default)
    {
        var response = await _http.GetAsync(package.TarballUrl, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
    }

    private async Task<JsonDocument> PackumentAsync(string name, CancellationToken token)
    {
        if (_packuments.TryGetValue(name, out var cached))
        {
            return cached;
        }

        // The abbreviated form is a fraction of the size and carries everything resolution needs.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_registry}/{Uri.EscapeDataString(name).Replace("%40", "@")}");
        request.Headers.Accept.ParseAdd("application/vnd.npm.install-v1+json");

        var response = await _http.SendAsync(request, token).ConfigureAwait(false);

        // A name that is not there is the ordinary mistake, usually a typo, and deserves to be
        // said rather than reported as an HTTP status code from somewhere inside a resolve.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new JsxCoreException($"There is no package named '{name}' on {_registry}.");
        }

        response.EnsureSuccessStatusCode();

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(token).ConfigureAwait(false)
            ?? throw new JsxCoreException($"The registry returned nothing for '{name}'.");

        _packuments[name] = document;
        return document;
    }

    private static IReadOnlyDictionary<string, string> ReadMap(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    // peerDependenciesMeta marks the peers a package can live without.
    private static IReadOnlySet<string> OptionalPeerNames(JsonElement entry) =>
        entry.TryGetProperty("peerDependenciesMeta", out var meta) && meta.ValueKind == JsonValueKind.Object
            ? meta.EnumerateObject()
                .Where(p => p.Value.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlyList<string> ReadList(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToList()
            : [];
}
