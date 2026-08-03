using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsxCore.TypeScript;

/// <summary>
/// Writes the TypeScript for a set of collected types. Discovers nothing: the set it is handed is
/// closed, so every reference it writes names something already declared.
/// </summary>
internal sealed class TypeScriptEmitter(
    TypeDefinitionOptions options,
    JsonSerializerOptions json,
    ModelMembers members,
    IReadOnlyDictionary<Type, DeclaredType> declared)
{
    private readonly Dictionary<string, string> _globalAliases = new(StringComparer.Ordinal);

    /// <summary>Which namespace block is being written, which decides how references are qualified.</summary>
    private string _currentNamespace = string.Empty;

    public GeneratedTypeScript Emit()
    {
        if (declared.Count == 0)
        {
            // Globals can still be worth describing when no model type is: one whose methods deal
            // only in primitives declares nothing, and is still callable.
            var only = GlobalsModule(rootNamespace: null);
            return new GeneratedTypeScript(only is null ? [] : [only]);
        }

        var namespaces = declared.Values
            .GroupBy(type => type.Namespace, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        // One root namespace and nothing outside it is the ordinary case, and the one worth
        // optimising for: the module can then export that root by assignment, which is what lets a
        // view default-import the assembly and reach a type through its .NET namespace, as
        // `MyApp.Models.Product`. Anything else falls back to exporting each root by name, because
        // a module can have one export assignment or many named exports, never both.
        var roots = namespaces
            .Where(group => group.Key.Length > 0)
            .Select(group => group.Key.Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var exportsByAssignment = roots.Count == 1 && namespaces.All(group => group.Key.Length > 0);

        var body = DeclarationBody(namespaces, exportsByAssignment ? string.Empty : "export ");

        var builder = new StringBuilder();
        AppendHeader(builder);
        AppendGlobalAliases(builder);

        builder.Append(body.TrimEnd()).AppendLine();

        if (exportsByAssignment)
        {
            builder.AppendLine().Append("export = ").Append(roots[0]).AppendLine(";");
        }

        var files = new List<GeneratedTypeScriptFile>
        {
            new(TypeDefinitionOptions.TypesFileName, TypeDefinitionOptions.TypesSpecifier, builder.ToString())
        };

        files.AddRange(NamespaceModules(namespaces, roots, exportsByAssignment));

        if (GlobalsModule(exportsByAssignment ? roots[0] : null) is { } globals)
        {
            files.Add(globals);
        }

        return new GeneratedTypeScript(files);
    }

    /// <summary>
    /// Every declaration, with types that have no .NET namespace at the top level of the module and
    /// everything else inside a namespace block mirroring its .NET one.
    /// </summary>
    private string DeclarationBody(
        IReadOnlyList<IGrouping<string, DeclaredType>> namespaces, string namespaceModifier)
    {
        var body = new StringBuilder();

        foreach (var group in namespaces)
        {
            var types = group.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

            if (group.Key.Length == 0)
            {
                foreach (var type in types)
                {
                    body.Append(Declare(type, "export ", indent: string.Empty)).AppendLine();
                }
                continue;
            }

            body.Append(namespaceModifier).Append("declare namespace ").Append(group.Key).AppendLine(" {");
            foreach (var type in types)
            {
                // Members of an ambient namespace are exported implicitly.
                body.Append(Declare(type, string.Empty, indent: "    "));
                if (type != types[^1])
                {
                    body.AppendLine();
                }
            }
            body.AppendLine("}").AppendLine();
        }

        return body.ToString();
    }

    /// <summary>
    /// Aliases for namespace-less types whose names are shadowed inside a namespace that references
    /// them; without these the reference would silently bind to the wrong type.
    /// </summary>
    /// <remarks>
    /// Written after the body, because writing the body is what discovers which ones are needed.
    /// </remarks>
    private void AppendGlobalAliases(StringBuilder builder)
    {
        if (_globalAliases.Count == 0)
        {
            return;
        }

        foreach (var (target, alias) in _globalAliases.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            builder.Append("type ").Append(alias).Append(" = ").Append(target).AppendLine(";");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// One module per .NET namespace, so a view can import from the namespace it means:
    /// <c>import { Product } from "dotnet:types/MyApp/Models"</c>.
    /// </summary>
    /// <remarks>
    /// Aliases onto the root module rather than re-declaring anything. Everything stays declared
    /// once, in one file, which is what keeps references between namespaces resolving; these are
    /// facades over it. They cost nothing at runtime either, being types, so no import map or
    /// module loader ever sees them.
    /// </remarks>
    private static IEnumerable<GeneratedTypeScriptFile> NamespaceModules(
        IEnumerable<IGrouping<string, DeclaredType>> namespaces,
        IReadOnlyList<string> roots,
        bool exportsByAssignment)
    {
        foreach (var group in namespaces.Where(group => group.Key.Length > 0))
        {
            var root = roots.First(candidate =>
                group.Key == candidate || group.Key.StartsWith(candidate + ".", StringComparison.Ordinal));

            // The whole .NET namespace, with no assembly to shed: two assemblies contributing to
            // one namespace contribute to one module, which is what they do in C# as well.
            var relative = group.Key;

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated>");
            builder.Append("//     Generated by JsxCore for the .NET namespace ").Append(group.Key).AppendLine(".");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine();

            builder.Append("import type ").Append(exportsByAssignment ? root : "{ " + root + " }")
                .Append(" from \"").Append(TypeDefinitionOptions.TypesSpecifier).AppendLine("\";");
            builder.AppendLine();

            // The imported identifier is the root namespace, so the qualifier below it is whatever
            // the .NET namespace adds on top.
            var qualifier = group.Key == root ? string.Empty : group.Key[(root.Length + 1)..] + ".";

            foreach (var type in group.OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                builder.Append("export type ").Append(type.Name).Append(" = ")
                    .Append(root).Append('.').Append(qualifier).Append(type.Name).AppendLine(";");
            }

            yield return new GeneratedTypeScriptFile(
                Path.Combine(
                    TypeDefinitionOptions.TypesModuleName,
                    relative.Replace('.', Path.DirectorySeparatorChar)) + ".d.ts",
                TypeDefinitionOptions.TypesSpecifier + "/" + relative.Replace('.', '/'),
                builder.ToString());
        }
    }

    private string Declare(DeclaredType type, string modifier, string indent)
    {
        _currentNamespace = type.Namespace;

        var text = type.Type.IsEnum ? DeclareEnum(type, modifier) : DeclareInterface(type, modifier);

        if (indent.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder();
        foreach (var line in text.TrimEnd().Split('\n'))
        {
            builder.Append(line.Length == 0 ? string.Empty : indent + line.TrimEnd('\r')).AppendLine();
        }
        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder)
    {
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("//     Generated by JsxCore from .NET types. Edits are overwritten on the next run.");
        builder.AppendLine("//");
        builder.Append("//     import type { Product } from \"")
            .Append(TypeDefinitionOptions.TypesSpecifier).AppendLine("/MyApp/Models\";");
        builder.AppendLine("//     ...or the whole tree, reached by .NET namespace:");
        builder.Append("//     import type Types from \"")
            .Append(TypeDefinitionOptions.TypesSpecifier).AppendLine("\";");
        builder.AppendLine("//");
        builder.AppendLine("//     These describe the model as it arrives in JavaScript, so they follow the");
        builder.AppendLine("//     application's JsonSerializerOptions rather than the .NET shape directly.");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine();
    }

    private string DeclareEnum(DeclaredType declaredType, string modifier)
    {
        var type = declaredType.Type;

        var values = Enum.GetNames(type)
            .Select(memberName => EmitEnumsAsStrings(type)
                ? $"\"{memberName}\""
                : Convert.ToInt64(Enum.Parse(type, memberName)).ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        AppendSummary(builder, type);
        builder.Append(modifier).Append("type ").Append(declaredType.Name).Append(" =").AppendLine();
        builder.Append("    | ").Append(string.Join($"{Environment.NewLine}    | ", values)).AppendLine(";");
        return builder.ToString();
    }

    private bool EmitEnumsAsStrings(Type type)
    {
        if (options.EnumsAsStrings is { } configured)
        {
            return configured;
        }

        // Follow whatever the application actually serialises with. Both spellings have to be
        // recognised: JsonStringEnumConverter<TEnum> is a JsonConverter<TEnum> and does not derive
        // from the non-generic JsonStringEnumConverter, so an assignability check alone misses it.
        if (type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is { } converter
            && IsStringEnumConverter(converter))
        {
            return true;
        }

        return json.Converters.Any(c => IsStringEnumConverter(c.GetType()));
    }

    private static bool IsStringEnumConverter(Type converter) =>
        converter == typeof(JsonStringEnumConverter)
        || (converter.IsGenericType && converter.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>))
        || typeof(JsonStringEnumConverter).IsAssignableFrom(converter);

    private string DeclareInterface(DeclaredType declaredType, string modifier)
    {
        var builder = new StringBuilder();
        AppendSummary(builder, declaredType.Type);
        builder.Append(modifier).Append("interface ").Append(declaredType.Name).AppendLine(" {");

        foreach (var member in members.Described(declaredType.Type))
        {
            var (memberType, nullable) = members.TypeOf(member);
            var reference = TypeReference(memberType);
            if (nullable)
            {
                reference += " | null";
            }

            builder.Append("    ").Append(JsonNameFor(member)).Append(nullable ? "?" : string.Empty)
                .Append(": ").Append(reference).AppendLine(";");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendSummary(StringBuilder builder, Type type) =>
        builder.Append("/** Generated from ").Append(type.FullName ?? type.Name).AppendLine(". */");

    private string JsonNameFor(MemberInfo member)
    {
        var explicitName = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        var name = explicitName ?? json.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;

        // Anything that is not a plain identifier has to be quoted to stay valid TypeScript.
        return IsValidIdentifier(name) ? name : $"\"{name.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] is not ('_' or '$')))
        {
            return false;
        }

        return name.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '_' or '$');
    }

    /// <summary>The TypeScript that describes a value of this .NET type, as JSON delivers it.</summary>
    private string TypeReference(Type type)
    {
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri))
        {
            return "string";
        }

        // System.Text.Json writes all of these as strings.
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(Version))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (TypeShape.IsNumeric(type))
        {
            return "number";
        }

        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument))
        {
            return "unknown";
        }

        // Byte arrays are base64-encoded rather than written as an array of numbers.
        if (type == typeof(byte[]))
        {
            return "string";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeReference(underlying) + " | null";
        }

        if (TypeShape.DictionaryValueType(type) is { } valueType)
        {
            return $"Record<string, {TypeReference(valueType)}>";
        }

        if (TypeShape.EnumerableElementType(type) is { } elementType)
        {
            var element = TypeReference(elementType);
            // Union members need parenthesising before the array suffix.
            return element.Contains('|', StringComparison.Ordinal) ? $"({element})[]" : $"{element}[]";
        }

        if (TypeShape.IsKeyValuePair(type))
        {
            var arguments = type.GetGenericArguments();
            return $"{{ key: {TypeReference(arguments[0])}; value: {TypeReference(arguments[1])} }}";
        }

        return declared.TryGetValue(type, out var match) ? ReferenceName(match) : "unknown";
    }

    private string ReferenceName(DeclaredType type)
    {
        if (type.Namespace == _currentNamespace)
        {
            return type.Name;
        }

        if (type.Namespace.Length > 0)
        {
            return type.QualifiedName;
        }

        // A namespace-less type referenced from inside a namespace resolves by simple name, unless
        // that namespace declares something of the same name, which would silently shadow it.
        var shadowed = declared.Values.Any(other =>
            other.Namespace == _currentNamespace && other.Name == type.Name);

        if (!shadowed)
        {
            return type.Name;
        }

        if (!_globalAliases.TryGetValue(type.Name, out var alias))
        {
            alias = type.Name + "$Global";
            _globalAliases[type.Name] = alias;
        }

        return alias;
    }

    /// <summary>
    /// The module naming every registered .NET global, so a view imports the one it wants rather
    /// than reaching through a catch-all object.
    /// </summary>
    /// <remarks>
    /// A service is described where it is used, as an inline object type, rather than being
    /// declared alongside the models: a service is not a model, and putting it in the assembly
    /// module would make it look like one. Its methods are what a view actually calls, so they are
    /// what gets described; the types they mention were collected before any of this ran.
    /// </remarks>
    private GeneratedTypeScriptFile? GlobalsModule(string? rootNamespace)
    {
        if (options.GlobalTypes.Count == 0)
        {
            return null;
        }

        // References are written from outside every namespace, because this is a different module:
        // the qualifier that would be redundant inside a namespace block is required here.
        _currentNamespace = string.Empty;

        var body = new StringBuilder();
        var referencesModels = false;

        // The escape hatch: reaches any global by name, including ones that cannot be an export.
        body.AppendLine("export declare const dotnet: Record<string, any>;");

        foreach (var (name, type) in options.GlobalTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (type is null)
            {
                // Registered with a factory, which says nothing about what it returns.
                body.Append("export declare const ").Append(name).AppendLine(": any;");
                continue;
            }

            body.Append("export declare const ").Append(name).AppendLine(": {");

            foreach (var method in TypeShape.CallableMethods(type))
            {
                var parameters = method.GetParameters().Select(parameter =>
                    $"{JsonNamingPolicy.CamelCase.ConvertName(parameter.Name ?? "arg")}: " +
                    TypeReference(parameter.ParameterType));

                var returns = method.ReturnType == typeof(void) ? "void" : TypeReference(method.ReturnType);

                // A method that takes nothing and returns nothing names no type, so it is not a
                // reason to import the types module. An unused import is not free: a project
                // compiling with noUnusedLocals rejects it.
                referencesModels |= method.GetParameters().Length > 0 || method.ReturnType != typeof(void);

                body.Append("    ").Append(JsonNamingPolicy.CamelCase.ConvertName(method.Name))
                    .Append('(').Append(string.Join(", ", parameters)).Append("): ").Append(returns).AppendLine(";");
            }

            foreach (var property in TypeShape.ReadableProperties(type)
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                referencesModels = true;

                body.Append("    ").Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                    .Append(": ").Append(TypeReference(property.PropertyType)).AppendLine(";");
            }

            body.AppendLine("};");
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("//     Generated by JsxCore from the globals registered with options.Globals.");
        builder.AppendLine("//");
        builder.AppendLine("//     import { Inventory } from \"dotnet:globals\";");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine();

        // Only imported when something is actually referenced, so the module does not depend on a
        // types module that may describe nothing.
        if (referencesModels && rootNamespace is not null)
        {
            builder.Append("import type ").Append(rootNamespace).Append(" from \"")
                .Append(TypeDefinitionOptions.TypesSpecifier).AppendLine("\";");
            builder.AppendLine();
        }

        builder.Append(body);

        return new GeneratedTypeScriptFile(
            TypeDefinitionOptions.GlobalsFileName, TypeDefinitionOptions.GlobalsSpecifier, builder.ToString());
    }
}
