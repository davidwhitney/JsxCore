using System.Runtime.CompilerServices;

namespace JsxCore.Tests.Fixtures;

internal static class TestSuiteBootstrap
{
    /// <summary>
    /// Installs the repository's npm packages once, before any test runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Several tests read the repository's node_modules directly: the module resolver ones resolve
    /// real packages out of it, and the bootstrapper ones assert that nothing is missing. Left to
    /// restore lazily, they pass or fail on whether some other test happened to trigger it first,
    /// which is a race that shows up as unrelated failures on a clean checkout.
    /// </para>
    /// <para>
    /// A module initializer is the one hook that reliably runs before the first test. Failure is
    /// swallowed deliberately: a test that needed the packages then fails with its own message,
    /// which is far easier to read than every test in the assembly failing to load.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void EnsurePackages()
    {
        try
        {
            JsxProjectFixture.EnsureRepositoryPackages();
        }
        catch (Exception)
        {
            // Reported by whichever test actually needs them.
        }
    }
}
