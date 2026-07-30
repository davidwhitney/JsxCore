using JsxCore;
using JsxCore.Compilation;
using JsxCore.Compilation.Provisioning;
using JsxCore.Compilation.Provisioning.PackageManagement;

namespace JsxCore.Tool;

public static class ProvisionCommand
{
    public const int Satisfied = 0;
    public const int Unresolved = 3;
    public const int InstallationDisabled = 4;
    public const int NoPackageManager = 5;
    public const int UnsupportedFramework = 6;

    public static int Run(Arguments arguments)
    {
        var projectDirectory = arguments.Required("project-dir");

        var options = new JsxCoreOptions { NpmPath = arguments.Optional("npm-path") };

        var framework = ConfiguredFramework.Parse(arguments.Optional("framework")) ?? JsFramework.Preact;

        if (arguments.Optional("framework") is { Length: > 0 } named && ConfiguredFramework.Parse(named) is null)
        {
            Console.Error.WriteLine(
                $"JsxCore does not know the framework '{named}'. " +
                $"<JsxCoreFramework> takes 'preact' or 'react'.");

            return UnsupportedFramework;
        }

        var directory = arguments.Optional("manifest-dir")
            ?? NearestManifestDirectory(projectDirectory);

        var required = RequiredPackages(options, framework);
        var outstanding = required
            .Where(package => !Installed(directory, package.Name))
            .Select(package => package.Name)
            .Concat(DependencyRestorer.DeclaredButAbsent(directory))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (outstanding.Count == 0)
        {
            return Satisfied;
        }

        if (!arguments.Flag("auto-install"))
        {
            Console.Error.WriteLine(
                $"JsxCore needs npm packages that are not installed: {string.Join(", ", outstanding)}. " +
                $"Automatic installation is off, so run an install in {directory} yourself, " +
                $"or set JsxCoreAutoInstallDependencies to true.");
            return InstallationDisabled;
        }

        var selector = PackageManagerSelector.Default(options, Console.WriteLine);
        var manager = selector.Select(arguments.Optional("package-manager"));

        if (manager is null)
        {
            Console.Error.WriteLine(
                $"JsxCore could not restore {string.Join(", ", outstanding)}: none of its package managers " +
                $"can run here ({selector.DescribeAll()}). Install Node.js so npm is on PATH, " +
                $"or set JsxCoreNpm to its location.");
            return NoPackageManager;
        }

        Console.WriteLine($"JsxCore: restoring with {manager.Name}: {string.Join(", ", outstanding)}");

        var result = new DependencyRestorer(manager, Console.WriteLine).Restore(directory, required);

        if (result.Failure is { } failure)
        {
            Console.Error.WriteLine($"JsxCore: restore failed: {failure}");
        }

        if (result.Missing.Count > 0)
        {
            Console.Error.WriteLine($"JsxCore could not install: {string.Join(", ", result.Missing)}.");
            return Unresolved;
        }

        return Satisfied;
    }

    private static IReadOnlyList<PackageRequest> RequiredPackages(JsxCoreOptions options, JsFramework framework)
    {
        var packages = new List<PackageRequest>
        {
            new("typescript", $"^{options.MinimumTypeScriptMajorVersion}", Development: true)
        };

        // Preact is not here: it ships inside JsxCore, so an application never has to install it
        // and never publishes it. Installing one anyway is still honoured, and takes precedence.
        //
        // React is, because it does not ship with anything: it comes from npm, and it publishes no
        // type declarations of its own, so the DefinitelyTyped packages are needed to compile a
        // view at all.
        if (framework == JsFramework.React)
        {
            packages.Add(new PackageRequest("react"));
            packages.Add(new PackageRequest("react-dom"));
            packages.Add(new PackageRequest("@types/react", string.Empty, Development: true));
            packages.Add(new PackageRequest("@types/react-dom", string.Empty, Development: true));
        }

        return packages;
    }

    private static bool Installed(string directory, string name) =>
        File.Exists(Path.Combine(directory, "node_modules", name, "package.json"));

    private static string NearestManifestDirectory(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "package.json")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return Path.GetFullPath(start);
    }
}
