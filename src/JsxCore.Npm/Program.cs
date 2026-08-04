using JsxCore.Npm;

// Commands are deliberately thin: each parses its arguments and hands off to a class that can be
// tested without a process. Failure is reported on stderr with a non-zero exit code.
try
{
    return args switch
    {
        // npm's own short aliases are accepted alongside the dotnet-shaped verbs, because muscle
        // memory types "i" and "ls".
        ["add", ..] => PackageCommands.Add(CommandLine.Parse(args)),
        ["remove" or "uninstall" or "rm" or "un", ..] => PackageCommands.Remove(CommandLine.Parse(args)),
        ["list" or "ls", ..] => PackageCommands.List(CommandLine.Parse(args)),
        ["init", ..] => PackageCommands.Init(CommandLine.Parse(args)),
        ["restore", ..] => PackageCommands.Restore(CommandLine.Parse(args)),

        // npm spells both of these "install": with a package it adds, without one it restores.
        ["install" or "i", _, ..] => PackageCommands.Add(CommandLine.Parse(args)),
        ["install" or "i"] => PackageCommands.Restore(CommandLine.Parse(args)),

        // npm ci restores from the lock file and fails rather than resolving without one.
        ["ci", ..] => PackageCommands.Restore(CommandLine.Parse(args), lockFileOnly: true),

        _ => PackageCommands.Help()
    };
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
