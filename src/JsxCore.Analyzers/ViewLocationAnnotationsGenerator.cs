using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace JsxCore.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class ViewLocationAnnotationsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
        {
            var global = provider.GlobalOptions;
            return new ViewLocationSettings(
                Enabled: Read(global, "JsxCoreEmitViewLocationAnnotations") is not "false",
                ViewsDirectory: Read(global, "JsxCoreViewsDirectory") ?? "Views",
                Extensions: Split(Read(global, "JsxCoreViewExtensions")) ?? ViewLocationAnnotations.DefaultExtensions,
                ViewFormats: Split(Read(global, "JsxCoreViewLocationFormats")) ?? ViewLocationAnnotations.DefaultViewLocationFormats,
                AreaFormats: Split(Read(global, "JsxCoreAreaViewLocationFormats")) ?? ViewLocationAnnotations.DefaultAreaViewLocationFormats,
                DefineAttributes: Read(global, "JsxCoreDefinesAnnotationAttributes") is not "false");
        });

        context.RegisterSourceOutput(settings, static (production, setting) =>
        {
            if (!setting.Enabled)
            {
                return;
            }

            production.AddSource(
                "JsxCoreViewLocations.g.cs",
                SourceText.From(ViewLocationAnnotations.Emit(setting), Encoding.UTF8));
        });
    }

    private static string? Read(AnalyzerConfigOptions options, string name) =>
        options.TryGetValue("build_property." + name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string[]? Split(string? value) =>
        value?.Split([';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim()).Where(part => part.Length > 0).ToArray() is { Length: > 0 } parts
            ? parts
            : null;
}
