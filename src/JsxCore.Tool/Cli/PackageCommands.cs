using System.Text.Json;
using System.Text.Json.Nodes;
using JsxCore.Compilation.Modules;
using JsxCore.Compilation.Provisioning.PackageManagement;
using JsxCore.Compilation.Provisioning.PackageManagement.Native;

namespace JsxCore.Tool.Cli;

/// <summary>
/// The npm side of JsxCore as a dotnet CLI tool, shaped like <c>dotnet package add</c>.
/// </summary>
/// <remarks>
/// Every command runs the same <see cref="NativePackageManager"/> the build uses, so a package
/// added here and a package restored by a build go through one implementation. Nothing shells out
/// to npm, and npm does not have to be installed.
/// </remarks>
public static class PackageCommands
{
    public static int Add(CommandLine command)
    {
        var directory = Directory(command);
        var requested = command.Positional.Skip(1).ToList();

        if (requested.Count == 0)
        {
            return Fail("a package name is required: dotnet npm add <PACKAGE_NAME>");
        }

        var version = command.Value("version", "v");

        if (version is not null && requested.Count > 1)
        {
            // Which of the three packages the single version belongs to is unanswerable, and
            // guessing would install the wrong thing quietly.
            return Fail("--version applies to one package; give each package its own range instead, as marked@^12");
        }

        var development = command.Has("dev", "save-dev", "D");

        var packages = requested
            .Select(specifier => Requested(specifier, version, development))
            .ToList();

        var manager = Manager(command);
        manager.CreateManifest(directory);

        var result = manager.Add(directory, packages);

        if (!result.Succeeded)
        {
            return Fail($"could not {result.Description}: {result.Failure}");
        }

        foreach (var package in packages)
        {
            Console.WriteLine($"Added '{package.Name}' to {(development ? "devDependencies" : "dependencies")}.");
        }

        return 0;
    }

    public static int Remove(CommandLine command)
    {
        var directory = Directory(command);
        var names = command.Positional.Skip(1).ToList();

        if (names.Count == 0)
        {
            return Fail("a package name is required: dotnet npm remove <PACKAGE_NAME>");
        }

        var path = Path.Combine(directory, "package.json");

        if (!File.Exists(path))
        {
            return Fail($"there is no package.json in '{directory}'.");
        }

        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var removed = new List<string>();

        foreach (var name in names)
        {
            foreach (var block in new[] { "dependencies", "devDependencies" })
            {
                if (manifest[block] is JsonObject declared && declared.Remove(name))
                {
                    removed.Add(name);
                }
            }
        }

        if (removed.Count == 0)
        {
            Console.WriteLine($"No package named '{string.Join("', '", names)}' is declared in {path}.");
            return 0;
        }

        File.WriteAllText(path, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        // The tree is resolved again from what is left, which rewrites the lock file without the
        // removed package or anything that was only there for it. Its own directory goes now;
        // transitive packages it brought in stay on disk until node_modules is restored fresh.
        foreach (var name in removed)
        {
            var installed = Path.Combine(directory, "node_modules", name.Replace('/', Path.DirectorySeparatorChar));

            if (System.IO.Directory.Exists(installed))
            {
                System.IO.Directory.Delete(installed, recursive: true);
            }
        }

        var result = Manager(command).InstallDeclared(directory);

        if (!result.Succeeded)
        {
            return Fail($"could not {result.Description}: {result.Failure}");
        }

        foreach (var name in removed.Distinct(StringComparer.Ordinal))
        {
            Console.WriteLine($"Removed '{name}'.");
        }

        return 0;
    }

    /// <param name="lockFileOnly">
    /// Refuse to resolve, as npm ci does. Restoring exactly what is pinned and quietly resolving
    /// something else when nothing is pinned are different promises, and a command named after the
    /// first should not deliver the second.
    /// </param>
    public static int Restore(CommandLine command, bool lockFileOnly = false)
    {
        var directory = Directory(command);
        var manager = Manager(command);

        // The lock file first, because installing exactly what is pinned is reproducible and
        // leaves the file alone. Resolving afresh is what happens when there is nothing pinned.
        var result = manager.RestoreFromLockFile(directory);

        if (result == PackageOperationResult.NothingToDo)
        {
            if (lockFileOnly)
            {
                return Fail($"there is no package-lock.json in '{directory}'. Run 'dotnet npm restore' to write one.");
            }

            result = manager.InstallDeclared(directory);
        }

        if (!result.Succeeded)
        {
            return Fail($"could not {result.Description}: {result.Failure}");
        }

        Console.WriteLine(result.Description);
        return 0;
    }

    public static int List(CommandLine command)
    {
        var directory = Directory(command);
        var manifest = PackageManifest.In(directory);

        if (manifest is null)
        {
            return Fail($"there is no package.json in '{directory}'.");
        }

        if (manifest.Packages.Count == 0)
        {
            Console.WriteLine("No packages are declared.");
            return 0;
        }

        var width = manifest.Packages.Max(package => package.Name.Length);

        foreach (var package in manifest.Packages.OrderBy(p => p.Development).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var installed = System.IO.Directory.Exists(
                Path.Combine(directory, "node_modules", package.Name.Replace('/', Path.DirectorySeparatorChar)));

            var line =
                $"{package.Name.PadRight(width)}  {package.Range.PadRight(12)}  " +
                $"{(package.Development ? "dev" : "   ")}  {(installed ? "" : "not installed")}";

            Console.WriteLine(line.TrimEnd());
        }

        return 0;
    }

    public static int Init(CommandLine command)
    {
        var directory = Directory(command);
        var result = Manager(command).CreateManifest(directory);

        if (!result.Succeeded)
        {
            return Fail($"could not {result.Description}: {result.Failure}");
        }

        if (result == PackageOperationResult.NothingToDo)
        {
            Console.WriteLine($"{Path.Combine(directory, "package.json")} already exists.");
        }

        return 0;
    }

    public static int Help()
    {
        Console.WriteLine(
            """
            JsxCore's npm client. Restores npm packages without npm or Node installed.

            Usage:
              dotnet npm add <PACKAGE_NAME> [--version <RANGE>] [--dev]
              dotnet npm remove <PACKAGE_NAME>
              dotnet npm list
              dotnet npm restore
              dotnet npm ci
              dotnet npm init

            Options:
              --version <RANGE>   Version range to add, as ^12 or 12.0.1. Defaults to the latest
                                  release. A range on the name, as marked@^12, works too.
              --dev               Add to devDependencies rather than dependencies.
              --project <PATH>    Project or directory whose package.json to use. Defaults to the
                                  current directory.
              --registry <URL>    Registry to resolve from. Defaults to registry.npmjs.org.

            npm's own spellings work too: install, i, ls, rm and un.

            Packages in dependencies are served to the browser; packages in devDependencies are
            build and server only. See https://github.com/davidwhitney/JsxCore for the rest.
            """);

        return 0;
    }

    private static PackageRequest Requested(string specifier, string? version, bool development)
    {
        if (version is not null)
        {
            return new PackageRequest(specifier, version, development);
        }

        // A scoped name starts with @, so the separating @ is the one after the scope.
        var separator = specifier.LastIndexOf('@');

        return separator > 0
            ? new PackageRequest(specifier[..separator], specifier[(separator + 1)..], development)
            : new PackageRequest(specifier, string.Empty, development);
    }

    private static NativePackageManager Manager(CommandLine command) =>
        new(registryUrl: command.Value("registry") ?? "https://registry.npmjs.org",
            report: Console.WriteLine);

    /// <summary>
    /// Where the package.json is. A project file is accepted for symmetry with
    /// <c>dotnet package add --project</c>, and resolves to the directory holding it.
    /// </summary>
    private static string Directory(CommandLine command)
    {
        var given = command.Value("project", "prefix");

        if (given is null)
        {
            return System.IO.Directory.GetCurrentDirectory();
        }

        var full = Path.GetFullPath(given);

        return File.Exists(full) ? Path.GetDirectoryName(full)! : full;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"JsxCore: {message}");
        return 1;
    }
}
