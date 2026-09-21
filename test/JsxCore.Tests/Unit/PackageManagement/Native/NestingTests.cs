using System.Net;
using System.Text;
using System.Text.Json;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;
using Shouldly;

namespace JsxCore.Tests.Unit.PackageManagement.Native;

// Where a nested copy lands, on graphs small enough to reason about and served from memory, so
// the shape under test is the test's to decide rather than whatever the registry holds today.
public class NestingTests
{
    [Fact]
    public async Task Resolve_NestedCopyWouldShadowItsOwnerDependency_NestsItOneLevelDeeper()
    {
        // The shape jest 30.5 took in August 2026. glob 13 at the top wants minimatch 10, so
        // minimatch 10 sits at the top. test-exclude wants glob 10, which has to nest, and wants
        // minimatch 10, which it finds at the top. The nested glob 10 wants minimatch 9. The first
        // free scope for that 9 is test-exclude's own node_modules, and a copy there would be what
        // test-exclude itself resolves minimatch to, so it has to go one level down, inside glob.
        var registry = new InMemoryRegistry()
            .Publish("glob", "13.0.6", dependencies: new { minimatch = "^10.2.2" })
            .Publish("glob", "10.5.0", dependencies: new { minimatch = "^9.0.4" })
            .Publish("minimatch", "10.2.6")
            .Publish("minimatch", "9.0.9")
            .Publish("test-exclude", "7.0.2", dependencies: new { glob = "^10.4.1", minimatch = "^10.2.2" });

        var placed = await new PackageResolver(registry.Client()).ResolveAsync(
            [new PackageRequest("glob", "^13.0.0"), new PackageRequest("test-exclude", "^7.0.0")]);

        PackageResolver.Validate(placed).ShouldBeEmpty();

        var paths = placed.ToDictionary(p => p.Path, p => p.Package.Version.ToString());
        paths["node_modules/minimatch"].ShouldBe("10.2.6");
        paths["node_modules/test-exclude/node_modules/glob"].ShouldBe("10.5.0");
        paths["node_modules/test-exclude/node_modules/glob/node_modules/minimatch"].ShouldBe("9.0.9");
        paths.ShouldNotContainKey("node_modules/test-exclude/node_modules/minimatch");
    }

    [Fact]
    public async Task Resolve_NestedCopyShadowsNothing_StaysAtTheShallowestFreeScope()
    {
        // The same graph without test-exclude's own claim on minimatch: now nothing in the way of
        // the shallower slot, and nesting deeper than that would duplicate the package for every
        // dependent sharing that ancestor.
        var registry = new InMemoryRegistry()
            .Publish("glob", "13.0.6", dependencies: new { minimatch = "^10.2.2" })
            .Publish("glob", "10.5.0", dependencies: new { minimatch = "^9.0.4" })
            .Publish("minimatch", "10.2.6")
            .Publish("minimatch", "9.0.9")
            .Publish("test-exclude", "7.0.2", dependencies: new { glob = "^10.4.1" });

        var placed = await new PackageResolver(registry.Client()).ResolveAsync(
            [new PackageRequest("glob", "^13.0.0"), new PackageRequest("test-exclude", "^7.0.0")]);

        PackageResolver.Validate(placed).ShouldBeEmpty();

        var paths = placed.ToDictionary(p => p.Path, p => p.Package.Version.ToString());
        paths["node_modules/test-exclude/node_modules/minimatch"].ShouldBe("9.0.9");
        paths.ShouldNotContainKey("node_modules/test-exclude/node_modules/glob/node_modules/minimatch");
    }

    // Answers the abbreviated packument the registry client asks for, from a graph published in
    // the test.
    private sealed class InMemoryRegistry : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, object>> _versions = new(StringComparer.Ordinal);

        public InMemoryRegistry Publish(string name, string version, object? dependencies = null)
        {
            if (!_versions.TryGetValue(name, out var versions))
            {
                _versions[name] = versions = new Dictionary<string, object>(StringComparer.Ordinal);
            }

            versions[version] = new
            {
                name,
                version,
                dependencies = dependencies ?? new { },
                dist = new { tarball = $"memory://{name}/{version}.tgz", integrity = "" }
            };

            return this;
        }

        public NpmRegistry Client() => new(new HttpClient(this), "memory://registry");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.TrimStart('/'));

            if (!_versions.TryGetValue(name, out var versions))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = JsonSerializer.Serialize(new { name, versions });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
