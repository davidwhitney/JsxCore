using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace JsxCore.Analyzers;

/// <summary>
/// Finds <c>options.Globals.Register(...)</c> calls and records them on the assembly, so the build
/// can describe <c>dotnet:globals</c> without running the application. See
/// <see cref="GlobalRegistrationAnnotations"/> for why.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GlobalRegistrationGenerator : IIncrementalGenerator
{
    private const string RegistryType = "JsxCore.Interop.JsxGlobalRegistry";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var registrations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Register" }
                },
                static (syntax, cancellationToken) => Read(syntax, cancellationToken))
            .Where(static found => found is not null)
            .Select(static (found, _) => found!.Value)
            .Collect();

        context.RegisterSourceOutput(registrations, static (production, found) =>
        {
            // Nothing recognised means either an application with no globals or one whose
            // registrations are somewhere this cannot see. Both want the old behaviour, so say
            // nothing at all rather than claiming an empty set is the answer.
            if (found.IsDefaultOrEmpty)
            {
                return;
            }

            var complete = found.All(entry => entry.Understood);

            production.AddSource(
                "JsxCoreGlobals.g.cs",
                SourceText.From(
                    GlobalRegistrationAnnotations.Emit(
                        found.Where(entry => entry.Understood)
                            .Select(entry => new RegisteredGlobal(entry.Name!, entry.TypeName))
                            .ToList(),
                        complete),
                    Encoding.UTF8));
        });
    }

    private readonly record struct Found(bool Understood, string? Name, string TypeName);

    private static Found? Read(GeneratorSyntaxContext syntax, System.Threading.CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)syntax.Node;

        if (syntax.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingType?.ToDisplayString() != RegistryType)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;

        // Register<TService>(name?)
        if (method.TypeArguments.Length == 1)
        {
            var serviceType = method.TypeArguments[0];

            // The name defaults to the type's own name, exactly as the overload does.
            if (arguments.Count == 0)
            {
                return new Found(true, serviceType.Name, FullName(serviceType));
            }

            return LiteralName(arguments[0], syntax, cancellationToken) is { } named
                ? new Found(true, named, FullName(serviceType))
                : new Found(false, null, "");
        }

        // Register(name, instance) and Register(name, factory). The instance overload knows a type
        // at run time, which is not necessarily the one written here, and the factory overload
        // knows none. Both are recorded by name alone and described as any, which is what the
        // running application does with them too.
        if (arguments.Count == 2)
        {
            return LiteralName(arguments[0], syntax, cancellationToken) is { } named
                ? new Found(true, named, "")
                : new Found(false, null, "");
        }

        return new Found(false, null, "");
    }

    /// <summary>
    /// The name, when it is written down. A computed one is knowable only once it has been
    /// computed, which is to say when the application runs.
    /// </summary>
    private static string? LiteralName(
        ArgumentSyntax argument,
        GeneratorSyntaxContext syntax,
        System.Threading.CancellationToken cancellationToken)
    {
        var value = syntax.SemanticModel.GetConstantValue(argument.Expression, cancellationToken);

        return value is { HasValue: true, Value: string name } && name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Named the way <see cref="System.Type.FullName"/> would, because that is what loads it back:
    /// nested types are joined with '+', and nothing carries an assembly or a namespace alias.
    /// </summary>
    private static string FullName(ITypeSymbol type)
    {
        var parts = new List<string>();

        for (var current = type; current is not null; current = current.ContainingType)
        {
            parts.Insert(0, current.Name);
        }

        var containing = type.ContainingNamespace;
        var prefix = containing is null || containing.IsGlobalNamespace
            ? ""
            : containing.ToDisplayString() + ".";

        return prefix + string.Join("+", parts);
    }
}
