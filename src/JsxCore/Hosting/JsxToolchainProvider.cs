using JsxCore.Compilation.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JsxCore.Hosting;

/// <summary>
/// Installs the packages a build needs and confirms the TypeScript toolchain is usable, once.
/// </summary>
/// <remarks>
/// <para>
/// This used to happen inside <c>AddJsxCore</c>, which meant a package restore ran during service
/// registration: before the host existed, before logging was configured, with no cancellation and
/// nothing to await. Progress went to the console because there was no logger to send it to.
/// </para>
/// <para>
/// Now it happens when the host starts, which is still before the first request is served, so a
/// missing toolchain still stops the application rather than surfacing as a failed view. The work
/// is done once and cached: whichever of the startup service or a service factory asks first pays
/// for it, and the rest read the answer.
/// </para>
/// </remarks>
public sealed class JsxToolchainProvider(
    JsxCoreOptions options,
    IHostEnvironment environment,
    string contentRoot,
    ILogger<JsxToolchainProvider> logger)
{
    private readonly object _gate = new();
    private TypeScriptToolchain? _toolchain;
    private bool _resolved;

    /// <summary>
    /// The toolchain, provisioning it if that has not happened yet. Null when the application
    /// serves precompiled views, which need no compiler at all.
    /// </summary>
    /// <exception cref="JsxCoreEnvironmentException">
    /// The toolchain is missing or unusable, with a message naming what to install.
    /// </exception>
    public TypeScriptToolchain? Value
    {
        get
        {
            lock (_gate)
            {
                if (_resolved)
                {
                    return _toolchain;
                }

                // Install anything missing before checking, so a first run does not fail on a
                // package the developer had no way to know they needed.
                _toolchain = EnvironmentVerifier.Verify(
                    options, contentRoot, TryInstall(),
                    warning => logger.LogWarning("{Warning}", warning));
                _resolved = true;
                return _toolchain;
            }
        }
    }

    /// <param name="cancellationToken">
    /// Governs waiting for the work rather than the work itself: restoring packages is synchronous
    /// down in the package client, and its own timeout is what bounds it.
    /// </param>
    public Task<TypeScriptToolchain?> EnsureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Value, cancellationToken);

    /// <summary>
    /// Runs the package bootstrapper if configuration allows it, returning why it failed or null.
    /// </summary>
    /// <remarks>
    /// Never fatal by itself: whatever happens here, the verifier runs next and produces an
    /// actionable message. Skipped entirely for precompiled applications, which have no toolchain
    /// to install and no business writing to the file system on a server.
    /// </remarks>
    private string? TryInstall()
    {
        var allowed = options.AutoInstallDependencies switch
        {
            DependencyInstallMode.Always => true,
            DependencyInstallMode.Development => environment.IsDevelopment(),
            _ => false
        };

        if (!allowed || options.PrecompiledOnly == true)
        {
            return null;
        }

        var report = options.OnBootstrapMessage ?? (message => logger.LogInformation("{Message}", message));

        try
        {
            var bootstrapper = new NpmBootstrapper(report, options.DependencyInstallTimeout, options.NpmPath);
            return bootstrapper.EnsureDependencies(options, contentRoot).Failure;
        }
        catch (Exception exception) when (exception is not JsxCoreException)
        {
            return exception.Message;
        }
    }
}
