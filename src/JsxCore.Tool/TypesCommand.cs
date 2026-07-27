using JsxCore;
using JsxCore.Compilation;
using JsxCore.TypeScript;

namespace JsxCore.Tool;

public static class TypesCommand
{
    public static int Run(Arguments arguments)
    {
        var contentRoot = arguments.Required("project-dir");
        var assemblyPath = arguments.Required("assembly");

        var options = new JsxCoreOptions
        {
            ViewsDirectory = arguments.Required("views"),
            WorkingDirectory = arguments.Required("working")
        };

        var layout = CompilationLayout.Create(options, contentRoot);
        var result = BuildTimeModelTypes.Generate(assemblyPath, layout.GeneratedTypesDirectory);

        if (result.Generated)
        {
            Console.WriteLine($"JsxCore: generated TypeScript declarations for {result.TypeCount} .NET type(s).");
        }
        else if (result.Failure is { } failure)
        {
            // Not a build failure: views still compile, with model types as any, and the
            // application generates the real declarations when it runs.
            Console.WriteLine($"JsxCore: could not generate declarations from .NET types ({failure}).");
        }

        return 0;
    }
}
