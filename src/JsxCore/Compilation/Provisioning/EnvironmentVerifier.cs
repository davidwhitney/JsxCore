using System.Text;
using JsxCore.Compilation.Assets;
using JsxCore.Compilation.Modules;

namespace JsxCore.Compilation.Provisioning;

public static class EnvironmentVerifier
{
    public static TypeScriptToolchain? Verify(
        JsxCoreOptions options,
        string contentRoot,
        string? bootstrapFailure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        if (options.PrecompiledOnly)
        {
            VerifyPrecompiledOutput(options, contentRoot);
            return null;
        }

        var toolchain = TypeScriptToolchainLocator.Locate(
            contentRoot,
            options.TypeScriptCompilerPath,
            options.AdditionalToolchainSearchPaths);

        if (toolchain is null)
        {
            throw new JsxCoreEnvironmentException(
                ToolchainMissingMessage(options, contentRoot) + BootstrapNote(bootstrapFailure));
        }

        if (toolchain.MajorVersion < options.MinimumTypeScriptMajorVersion)
        {
            throw new JsxCoreEnvironmentException(
                $"JsxCore requires TypeScript {options.MinimumTypeScriptMajorVersion} or later, but found " +
                $"version {toolchain.Version} at '{toolchain.ExecutablePath}'.{Environment.NewLine}{Environment.NewLine}" +
                $"JsxCore depends on the native compiler and on 'rewriteRelativeImportExtensions', which " +
                $"earlier versions do not provide.{Environment.NewLine}" +
                $"Upgrade by raising the typescript version in package.json to ^7 and building, or with " +
                $"npm:{Environment.NewLine}{Environment.NewLine}    npm install --save-dev typescript@^7{Environment.NewLine}");
        }

        VerifyViewsDirectory(options, contentRoot);
        VerifyWorkingDirectory(options, contentRoot);
        VerifyRuntimePackages(options, contentRoot, bootstrapFailure);

        return toolchain;
    }

    /// <summary>
    /// Explains that JsxCore already tried to install the dependency, so the reader does not assume
    /// automatic installation would have fixed it.
    /// </summary>
    private static string BootstrapNote(string? bootstrapFailure) =>
        bootstrapFailure is null
            ? string.Empty
            : $"{Environment.NewLine}JsxCore tried to install this automatically and could not: {bootstrapFailure}";

    private static void VerifyRuntimePackages(JsxCoreOptions options, string contentRoot, string? bootstrapFailure)
    {
        // Preact ships inside JsxCore, so nothing has to be installed for it to render. Only a
        // package JsxCore does not carry can be missing, which today means none of them.
        var missing = new List<string>();
        foreach (var package in new[] { "preact", "preact-render-to-string" })
        {
            var installed = NodeModulesLayout.For(contentRoot, options.AdditionalToolchainSearchPaths)
                .FindPackage(package) is not null;

            if (!installed && !VendoredPreact.Versions.ContainsKey(package))
            {
                missing.Add(package);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        throw new JsxCoreEnvironmentException(
            $"JsxCore is configured to use Preact, but {string.Join(" and ", missing)} " +
            $"{(missing.Count == 1 ? "is" : "are")} not installed." +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"Build the project to install them, or install them yourself:{Environment.NewLine}{Environment.NewLine}" +
            $"    npm install preact preact-render-to-string{Environment.NewLine}{Environment.NewLine}" +
            $"Searched node_modules upwards from '{contentRoot}'.{Environment.NewLine}" +
            $"Uninstalling them is also a fix: JsxCore falls back to the copy inside the package." +
            BootstrapNote(bootstrapFailure));
    }

    /// <summary>
    /// In precompiled mode there is no compiler to check, so the thing that must exist is the
    /// compiled output itself. Failing here beats serving an application that 500s on every view.
    /// </summary>
    private static void VerifyPrecompiledOutput(JsxCoreOptions options, string contentRoot)
    {
        var layout = CompilationLayout.Create(options, contentRoot);

        if (Directory.Exists(layout.OutputDirectory)
            && Directory.EnumerateFiles(layout.OutputDirectory, "*.js", SearchOption.AllDirectories).Any())
        {
            return;
        }

        throw new JsxCoreEnvironmentException(
            $"JsxCore is configured with PrecompiledOnly, but no compiled views were found in " +
            $"'{layout.OutputDirectory}'.{Environment.NewLine}{Environment.NewLine}" +
            $"Compiled views are produced by the JsxCore MSBuild target during 'dotnet publish'. " +
            $"Check that the package's build targets ran, that JsxCoreCompileOnBuild was not set to " +
            $"false, and that JsxCoreOptions.WorkingDirectory matches JsxCoreWorkingDirectory.");
    }

    /// <summary>
    /// Confirms that precompiled views were compiled against the runtime the application is
    /// configured to use.
    /// </summary>
    /// <remarks>
    /// Elements built by one JSX factory cannot be rendered by another's renderer. The result
    /// is silently empty markup rather than an error, which is a miserable thing to debug. The build
    /// that produced the output has its own runtime setting (the JsxCoreRuntime MSBuild property),
    /// so the two can be configured independently and therefore can disagree.
    /// </remarks>
    private static void VerifyViewsDirectory(JsxCoreOptions options, string contentRoot)
    {
        var viewsPath = ContentRootPath.Resolve(options.ViewsDirectory, contentRoot);
        if (Directory.Exists(viewsPath))
        {
            return;
        }

        throw new JsxCoreEnvironmentException(
            $"JsxCore is configured to load views from '{viewsPath}', but that directory does not exist." +
            $"{Environment.NewLine}Create it, or set JsxCoreOptions.ViewsDirectory to the correct location.");
    }

    private static void VerifyWorkingDirectory(JsxCoreOptions options, string contentRoot)
    {
        var workingPath = ContentRootPath.Resolve(options.WorkingDirectory, contentRoot);

        try
        {
            Directory.CreateDirectory(workingPath);

            // Creating the directory can succeed on a read-only mount, so prove we can write.
            var probe = Path.Combine(workingPath, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new JsxCoreEnvironmentException(
                $"JsxCore could not write to its working directory '{workingPath}'. Compiled views are " +
                $"written there, so this must be writable by the application process." +
                $"{Environment.NewLine}Set JsxCoreOptions.WorkingDirectory to a writable location.", ex);
        }
    }

    private static string ToolchainMissingMessage(JsxCoreOptions options, string contentRoot)
    {
        var message = new StringBuilder();
        message.AppendLine("JsxCore could not find the TypeScript compiler, which it needs to compile .tsx and .jsx views.");
        message.AppendLine();
        message.AppendLine("Build the project to install it, which needs no other tooling, or install it yourself:");
        message.AppendLine();
        message.AppendLine("    npm install --save-dev typescript@^7");
        message.AppendLine();
        message.AppendLine($"JsxCore looks for the native compiler shipped by that package (@typescript/{TypeScriptToolchainLocator.PlatformPackageName()}).");

        if (!string.IsNullOrWhiteSpace(options.TypeScriptCompilerPath))
        {
            message.AppendLine();
            message.AppendLine($"JsxCoreOptions.TypeScriptCompilerPath was set to '{options.TypeScriptCompilerPath}', but no usable compiler was found there.");
            return message.ToString();
        }

        message.AppendLine();
        message.AppendLine("Paths searched:");
        foreach (var candidate in TypeScriptToolchainLocator.CandidatePaths(contentRoot, options.AdditionalToolchainSearchPaths))
        {
            message.AppendLine("    " + candidate);
        }
        message.AppendLine();
        message.AppendLine("Alternatively set JsxCoreOptions.TypeScriptCompilerPath to a compiler executable.");

        return message.ToString();
    }
}
