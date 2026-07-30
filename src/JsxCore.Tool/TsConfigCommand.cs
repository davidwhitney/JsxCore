using JsxCore;
using JsxCore.Compilation;
using JsxCore.Compilation.Assets;

namespace JsxCore.Tool;

public static class TsConfigCommand
{
    public static int Run(Arguments arguments)
    {
        var contentRoot = arguments.Required("project-dir");

        var options = new JsxCoreOptions
        {
            ViewsDirectory = arguments.Required("views"),
            WorkingDirectory = arguments.Required("working"),
            TypeChecking = arguments.Optional("type-checking") switch
            {
                "off" => TypeCheckingMode.Off,
                "error" => TypeCheckingMode.Error,
                _ => TypeCheckingMode.Warn
            }
        };

        foreach (var path in arguments.List("search-paths"))
        {
            options.AdditionalToolchainSearchPaths.Add(path);
        }

        var layout = CompilationLayout.Create(options, contentRoot);

        options.GenerateEditorTsConfig = arguments.Flag("editor-config");

        Directory.CreateDirectory(layout.WorkingDirectory);
        TsConfigWriter.WriteIfChanged(options, layout);

        // Only rewritten while it still carries JsxCore's marker, so a hand-edited one is left be.
        if (options.GenerateEditorTsConfig)
        {
            TsConfigWriter.WriteIdeConfigIfOwned(options, layout);
        }

        // The compiler resolves @jsxcore/runtime from declarations on disk, so they have to be
        // beside the configuration that points at them.
        RuntimeAssets.ExtractTypeDefinitions(layout.RuntimeDirectory);

        return 0;
    }
}
