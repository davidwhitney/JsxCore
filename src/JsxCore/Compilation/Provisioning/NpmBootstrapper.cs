using System.Diagnostics;
using System.Text;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;

namespace JsxCore.Compilation.Provisioning;

public sealed record NpmBootstrapResult(IReadOnlyList<string> Actions, string? Failure)
{
    public static readonly NpmBootstrapResult NothingToDo = new([], null);
    public bool DidAnything => Actions.Count > 0;
}

/// <summary>
/// Installs the npm packages JsxCore needs, so a first run does not fail on a missing compiler.
/// </summary>
/// <remarks>
/// <para>
/// This writes to the developer's project: it can create a <c>package.json</c> and it adds
/// packages and a lock file. That is a real side effect, so it only runs in Development by
/// default, never when serving precompiled output, and every step is logged as it happens.
/// </para>
/// <para>
/// A failure here is never fatal on its own. The environment verifier still runs afterwards and
/// produces its usual message, with the reason bootstrapping failed appended, so the developer
/// always ends up with an instruction they can follow by hand.
/// </para>
/// </remarks>
public sealed class NpmBootstrapper(Action<string> report, TimeSpan timeout, string? npmPath = null)
{
    private readonly Action<string> _report = report ?? throw new ArgumentNullException(nameof(report));

    public static IReadOnlyList<(string Package, string VersionRange, bool DevDependency)> RequiredPackages(
        JsxCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var packages = new List<(string, string, bool)>
        {
            ("typescript", $"^{options.MinimumTypeScriptMajorVersion}", true)
        };

        if (options.Runtime == JsxRuntimeMode.Preact)
        {
            packages.Add(("preact", string.Empty, false));
            packages.Add(("preact-render-to-string", string.Empty, false));
        }

        return packages;
    }

    public static IReadOnlyList<string> MissingPackages(JsxCoreOptions options, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(options);

        var missing = new List<string>();

        foreach (var (package, _, _) in RequiredPackages(options))
        {
            // TypeScript needs its platform-specific binary as well as the package itself, which is
            // what an interrupted or partial install typically leaves behind.
            var installed = package == "typescript"
                ? TypeScriptToolchainLocator.Locate(contentRoot, options.TypeScriptCompilerPath, options.AdditionalToolchainSearchPaths) is not null
                : NodeModulesLayout.For(contentRoot, options.AdditionalToolchainSearchPaths).FindPackage(package) is not null;

            if (!installed)
            {
                missing.Add(package);
            }
        }

        return missing;
    }

    public static (string Directory, bool HasManifest) ResolveProjectDirectory(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var directory = new DirectoryInfo(Path.GetFullPath(contentRoot));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "package.json")))
            {
                return (directory.FullName, true);
            }
            directory = directory.Parent;
        }

        return (Path.GetFullPath(contentRoot), false);
    }

    public NpmBootstrapResult EnsureDependencies(JsxCoreOptions options, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(options);

        var missing = MissingPackages(options, contentRoot);
        if (missing.Count == 0)
        {
            return NpmBootstrapResult.NothingToDo;
        }

        // The same strategies the build uses, in the same order: the native client first, so a
        // machine with no npm installs its dependencies at startup exactly as it does at build
        // time. Offering npm alone here made "Node is not required" true of one path and not the
        // other, which surfaced as a missing package rather than as anything mentioning npm.
        //
        // Built from this instance's settings rather than the options alone, so a caller that
        // constructed the bootstrapper with an explicit npm path still gets it.
        var selector = new PackageManagerSelector(
        [
            new NativePackageManager(report: _report),
            new NpmPackageManager(npmPath ?? options.NpmPath, timeout, _report)
        ]);

        var manager = selector.Select(options.PackageManager);
        if (manager is null)
        {
            return new NpmBootstrapResult(
                [], $"none of JsxCore's package managers can run here ({selector.DescribeAll()}).");
        }

        var (directory, _) = ResolveProjectDirectory(contentRoot);
        var required = RequiredPackages(options)
            .Where(package => missing.Contains(package.Package))
            .Select(package => new PackageRequest(package.Package, package.VersionRange, package.DevDependency))
            .ToList();

        var result = new DependencyRestorer(manager, _report).Restore(directory, required);

        if (result.Failure is { } failure)
        {
            return new NpmBootstrapResult(result.Actions, failure);
        }

        _report("JsxCore: dependencies installed.");
        return new NpmBootstrapResult(result.Actions, null);
    }

    /// <summary>Kept for callers that only want to know whether npm is present.</summary>
    public static string? LocateNpm(string? explicitPath = null) => NpmPackageManager.Find(explicitPath);
}
