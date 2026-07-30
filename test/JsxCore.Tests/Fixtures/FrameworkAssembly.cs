using System.Reflection;
using System.Reflection.Emit;

namespace JsxCore.Tests.Fixtures;

/// <summary>
/// An assembly stamped with a framework, as the build stamps a real application's.
/// </summary>
/// <remarks>
/// The framework is chosen in the project file and recorded on the assembly the build produces, so
/// a hosted test has to present an assembly that carries the answer. Emitting one says exactly what
/// is being simulated, where setting an option would be testing a back door that applications do
/// not have.
/// </remarks>
public static class FrameworkAssembly
{
    private static readonly Dictionary<JsFramework, Assembly> Built = new();

    public static Assembly For(JsFramework framework)
    {
        lock (Built)
        {
            if (Built.TryGetValue(framework, out var existing))
            {
                return existing;
            }

            var name = new AssemblyName($"JsxCore.Tests.Stamped.{framework}");
            var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["JsxCoreFramework", framework == JsFramework.React ? "react" : "preact"]));

            Built[framework] = assembly;
            return assembly;
        }
    }
}
