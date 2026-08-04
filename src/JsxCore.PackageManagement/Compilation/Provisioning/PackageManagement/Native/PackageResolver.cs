namespace JsxCore.Compilation.Provisioning.PackageManagement.Native;

// Where a package ends up on disk, which is not decided by the package alone. Two dependents
// needing incompatible versions is normal, and npm answers it by putting one of them inside the
// dependent that asked for it rather than at the top.
public sealed record PlacedPackage(
    string Path,
    string Scope,
    RegistryPackage Package,
    bool Development,
    bool Optional,
    string InstallName,
    GitSpecifier? Git = null,
    Workspace? Workspace = null)
{
    public bool IsLink => Workspace is not null;
    public string Name => InstallName;
    public bool IsAlias => !string.Equals(InstallName, Package.Name, StringComparison.Ordinal);
    public bool IsNested => Scope.Length > 0;
}

public sealed class PackageResolver(
    NpmRegistry registry,
    OverrideSet? overrides = null,
    IReadOnlyList<Workspace>? workspaces = null,
    HttpClient? http = null)
{
    private readonly NpmRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly OverrideSet _overrides = overrides ?? OverrideSet.Empty;
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    private readonly IReadOnlyDictionary<string, Workspace> _workspaces =
        (workspaces ?? []).ToDictionary(w => w.Name, StringComparer.Ordinal);

    public async Task<IReadOnlyList<PlacedPackage>> ResolveAsync(
        IReadOnlyCollection<PackageRequest> requested,
        CancellationToken token = default)
    {
        var placements = new Dictionary<string, PlacedPackage>(StringComparer.Ordinal);

        // Shallowest first, and alphabetically within a depth. That ordering is the whole of the
        // hoisting policy: whichever dependent asks first claims the top level slot, and everyone
        // who disagrees with it gets a nested copy. npm orders its own queue the same way, so
        // matching it here is what makes the two trees agree on which version ends up on top.
        var pending = new PriorityQueue<PlacedPackage, (int Depth, string Path)>();

        foreach (var request in requested.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            foreach (var placed in await PlaceAsync(
                         placements,
                         Apply(new Edge("", PackageSpecifier.Parse(request.Name, request.VersionRange),
                             request.Development, false), dependent: null),
                         token).ConfigureAwait(false))
            {
                pending.Enqueue(placed, (Depth(placed.Path), placed.Path));
            }
        }

        while (pending.TryDequeue(out var node, out _))
        {
            foreach (var edge in EdgesOf(node))
            {
                foreach (var placed in await PlaceAsync(
                             placements, Apply(edge, node.Name), token).ConfigureAwait(false))
                {
                    pending.Enqueue(placed, (Depth(placed.Path), placed.Path));
                }
            }
        }

        return placements.Values.OrderBy(p => p.Path, StringComparer.Ordinal).ToList();
    }

    private Edge Apply(Edge edge, string? dependent)
    {
        if (_overrides.RangeFor(edge.Specifier.InstallName, dependent) is not { } forced)
        {
            return edge;
        }

        return edge with { Specifier = PackageSpecifier.Parse(edge.Specifier.InstallName, forced) };
    }

    private static IEnumerable<Edge> EdgesOf(PlacedPackage node)
    {
        // An entry in optionalDependencies overrides one of the same name in dependencies.
        // TypeScript lists its per platform compilers in both.
        foreach (var (name, spec) in node.Package.Dependencies)
        {
            if (!node.Package.OptionalDependencies.ContainsKey(name))
            {
                yield return new Edge(node.Path, PackageSpecifier.Parse(name, spec), node.Development, node.Optional);
            }
        }

        // Resolved for every platform, not just this one: the lock file has to describe a tree that
        // works anywhere, and npm records them all for the same reason.
        foreach (var (name, spec) in node.Package.OptionalDependencies)
        {
            yield return new Edge(node.Path, PackageSpecifier.Parse(name, spec), node.Development, true);
        }


    }

    // Everything this edge caused to be placed: the package itself and any peers claimed with it.
    // All of them still need expanding, which is why they are returned rather than just recorded.
    private async Task<IReadOnlyList<PlacedPackage>> PlaceAsync(
        Dictionary<string, PlacedPackage> placements,
        Edge edge,
        CancellationToken token)
    {
        if (_workspaces.TryGetValue(edge.Name, out var workspace))
        {
            // A sibling in this repository is linked, not fetched, so a change to it is visible
            // immediately rather than after a publish.
            var linkPath = PathFor("", edge.Name);
            if (!placements.ContainsKey(linkPath))
            {
                placements[linkPath] = new PlacedPackage(
                    linkPath, "",
                    new RegistryPackage(workspace.Name, SemanticVersion.Parse(workspace.Version),
                        workspace.RelativePath, "", new Dictionary<string, string>(),
                        new Dictionary<string, string>(), [], [], new Dictionary<string, string>(),
                        new Dictionary<string, string>(), new HashSet<string>()),
                    edge.Development, edge.Optional, edge.Name, Workspace: workspace);
            }

            return [];
        }

        if (edge.Range.IsUnsupported && GitSpecifier.TryParse(edge.Range.Text, out var git))
        {
            return await PlaceFromGitAsync(placements, edge, git, token).ConfigureAwait(false);
        }

        if (edge.Range.IsUnsupported)
        {
            throw new JsxCoreException(
                $"JsxCore cannot resolve '{edge.Name}@{edge.Range}'. Only registry, git and " +
                $"workspace dependencies are supported; this needs npm.");
        }

        var scope = "";

        if (Visible(placements, edge.Scope, edge.Name) is { } already)
        {
            if (edge.Range.Satisfies(already.Package.Version))
            {
                Relax(placements, already, edge);
                return [];
            }

            // Visible but wrong, so it has to go somewhere below whatever is in the way. The
            // shallowest free scope, not simply the dependent's own, because nesting deeper than
            // necessary duplicates the package for every dependent that shares an ancestor.
            scope = ShallowestFreeScope(placements, edge.Scope, edge.Name);

            // A peer whose scope is already taken goes inside the package that asked for it, which
            // is the only place left that nothing else can see.
            if (scope == already.Scope && edge.DependentPath is { } dependent)
            {
                scope = ShallowestFreeScope(placements, dependent, edge.Name);
            }

            if (scope == already.Scope)
            {
                throw new JsxCoreException(
                    $"'{edge.Name}' is needed at both {already.Package.Version} and {edge.Range} " +
                    $"in the same place, which cannot be satisfied.");
            }
        }

        var described = await DescribeAsync(edge, token).ConfigureAwait(false);
        if (described is null)
        {
            return [];
        }

        var path = PathFor(scope, edge.Name);
        var placed = new PlacedPackage(
            path, scope, described, edge.Development, edge.Optional, edge.Specifier.InstallName);

        placements[path] = placed;
        var claimed = new List<PlacedPackage> { placed };

        // A peer is something the package expects to find beside it rather than underneath it, and
        // it is claimed the moment the package lands rather than when the package is expanded.
        // That timing decides which version of a contested peer reaches the top level, so getting
        // it wrong changes the tree even though every version is still satisfied.
        foreach (var (name, spec) in described.PeerDependencies)
        {
            // An optional peer is not installed for its own sake. It is there to pin a version if
            // something else brings the package in, so following it would add packages npm does not.
            if (described.OptionalPeers.Contains(name))
            {
                continue;
            }

            claimed.AddRange(await PlaceAsync(
                placements,
                Apply(new Edge(scope, PackageSpecifier.Parse(name, spec), edge.Development, edge.Optional, path),
                    described.Name),
                token).ConfigureAwait(false));
        }

        return claimed;
    }

    private async Task<IReadOnlyList<PlacedPackage>> PlaceFromGitAsync(
        Dictionary<string, PlacedPackage> placements,
        Edge edge,
        GitSpecifier git,
        CancellationToken token)
    {
        var path = PathFor(Visible(placements, edge.Scope, edge.Name) is null ? "" : edge.Scope, edge.Name);
        if (placements.ContainsKey(path))
        {
            return [];
        }

        // The archive is the package, so its own manifest is the only source for a version and a
        // dependency list. There is no packument to ask.
        var manifest = await PackageArchive.ReadManifestAsync(_http, git.ArchiveUrl, token).ConfigureAwait(false);

        var described = new RegistryPackage(
            edge.Name,
            SemanticVersion.TryParse(manifest.Version, out var version) ? version : SemanticVersion.Parse("0.0.0"),
            git.ArchiveUrl,
            "",
            manifest.Dependencies,
            new Dictionary<string, string>(StringComparer.Ordinal),
            [], [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var placed = new PlacedPackage(
            path, path[..Math.Max(0, path.LastIndexOf("/node_modules/", StringComparison.Ordinal))],
            described, edge.Development, edge.Optional, edge.Name, Git: git);

        placements[path] = placed;
        return [placed];
    }

    private static int Depth(string path) =>
        path.Split("node_modules/", StringSplitOptions.None).Length - 1;

    private async Task<RegistryPackage?> DescribeAsync(Edge edge, CancellationToken token)
    {
        IReadOnlyList<SemanticVersion> versions;
        try
        {
            versions = await _registry.VersionsAsync(edge.Specifier.RegistryName, token).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (edge.Optional)
        {
            // An optional dependency that cannot be reached is optional.
            return null;
        }

        var chosen = edge.Range.Best(versions);
        if (chosen is null)
        {
            return edge.Optional
                ? null
                : throw new JsxCoreException(
                    $"No published version of '{edge.Specifier.RegistryName}' satisfies {edge.Range}.");
        }

        return await _registry.DescribeAsync(edge.Specifier.RegistryName, chosen, token).ConfigureAwait(false)
               ?? throw new JsxCoreException(
                   $"The registry has no metadata for {edge.Specifier.RegistryName}@{chosen}.");
    }

    // Reached again by a path that is neither development nor optional, so it is neither.
    private static void Relax(Dictionary<string, PlacedPackage> placements, PlacedPackage placed, Edge edge)
    {
        if ((placed.Development && !edge.Development) || (placed.Optional && !edge.Optional))
        {
            placements[placed.Path] = placed with
            {
                Development = placed.Development && edge.Development,
                Optional = placed.Optional && edge.Optional
            };
        }
    }

    // What a package sitting at a scope can actually import: its own node_modules first, then each
    // ancestor's, ending at the top level. This is Node's own resolution, which is why the tree can
    // be flattened at all.
    private static PlacedPackage? Visible(
        IReadOnlyDictionary<string, PlacedPackage> placements,
        string scope,
        string name)
    {
        var current = scope;
        while (true)
        {
            if (placements.TryGetValue(PathFor(current, name), out var found))
            {
                return found;
            }

            if (current.Length == 0)
            {
                return null;
            }

            current = placements.TryGetValue(current, out var owner) ? owner.Scope : "";
        }
    }

    private static string ShallowestFreeScope(
        IReadOnlyDictionary<string, PlacedPackage> placements,
        string from,
        string name)
    {
        var chain = new List<string>();
        var current = from;
        while (true)
        {
            chain.Add(current);
            if (current.Length == 0)
            {
                break;
            }
            current = placements.TryGetValue(current, out var owner) ? owner.Scope : "";
        }

        chain.Reverse();
        foreach (var scope in chain)
        {
            if (!placements.ContainsKey(PathFor(scope, name)))
            {
                return scope;
            }
        }

        return from;
    }

    private static string PathFor(string scope, string name) =>
        (scope.Length == 0 ? "" : scope + "/") + "node_modules/" + name;

    // Whether the tree actually works: every dependency of every placed package has to resolve,
    // from where that package sits, to a version that satisfies it. This is the property Node
    // relies on at run time, and the one thing that must never be wrong. Which version ends up
    // hoisted is a choice; this is not.
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<PlacedPackage> placed,
        OverrideSet? overrides = null)
    {
        var rules = overrides ?? OverrideSet.Empty;
        var byPath = placed.ToDictionary(p => p.Path, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var package in placed)
        {
            foreach (var (name, spec) in package.Package.Dependencies)
            {
                // An override replaces what the dependent asked for, so the declared range is no
                // longer the thing to check against. Forcing a version a dependent did not ask for
                // is the entire purpose of an override.
                var effective = rules.RangeFor(name, package.Name) ?? spec;
                var specifier = PackageSpecifier.Parse(name, effective);

                // A link or a repository has no version to compare, and nothing to check.
                if (specifier.Range.IsUnsupported)
                {
                    continue;
                }

                var found = Visible(byPath, package.Path, specifier.InstallName);
                if (found is null)
                {
                    problems.Add($"{package.Path} needs '{specifier.InstallName}' and nothing is visible to it.");
                }
                else if (!specifier.Range.Satisfies(found.Package.Version))
                {
                    problems.Add(
                        $"{package.Path} needs '{specifier.InstallName}' {specifier.Range} " +
                        $"but resolves to {found.Package.Version} at {found.Path}.");
                }
            }
        }

        return problems;
    }

    public static IEnumerable<PlacedPackage> InstallableOn(
        IEnumerable<PlacedPackage> placed,
        bool includeDevelopment = true) =>
        placed.Where(p => p.Package.RunsHere() && (includeDevelopment || !p.Development));

    // Scope is where the package should go; DependentPath is who asked. They differ for a peer,
    // which is placed beside its dependent rather than underneath it.
    private sealed record Edge(
        string Scope,
        PackageSpecifier Specifier,
        bool Development,
        bool Optional,
        string? DependentPath = null)
    {
        public string Name => Specifier.InstallName;
        public VersionRange Range => Specifier.Range;
    }
}
