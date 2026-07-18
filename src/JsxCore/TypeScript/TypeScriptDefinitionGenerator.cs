using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsxCore.TypeScript;

public sealed class TypeScriptDefinitionGenerator(TypeDefinitionOptions options, JsonSerializerOptions json)
{
    private readonly TypeDefinitionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly JsonSerializerOptions _json = json ?? throw new ArgumentNullException(nameof(json));
    private readonly NullabilityInfoContext _nullability = new();

    private readonly Dictionary<Type, DeclaredType> _declared = [];
    private readonly Dictionary<string, string> _globalAliases = new(StringComparer.Ordinal);

    private string _currentNamespace = string.Empty;

    private sealed record DeclaredType(Type Type, string Namespace, string Name)
    {
        public string QualifiedName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
    }

    public GeneratedTypeScript Generate()
    {
        _declared.Clear();
        _globalAliases.Clear();

        foreach (var type in _options.ResolveTypes())
        {
            Collect(type);
        }

        if (_declared.Count == 0)
        {
            return new GeneratedTypeScript([]);
        }

        var namespaces = _declared.Values
            .GroupBy(declared => declared.Namespace, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var body = new StringBuilder();

        // Types with no .NET namespace sit at the top level of the module; everything else goes
        // into a namespace block mirroring its .NET one.
        foreach (var group in namespaces)
        {
            var types = group.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

            if (group.Key.Length == 0)
            {
                foreach (var declared in types)
                {
                    body.Append(Declare(declared, "export ", indent: string.Empty)).AppendLine();
                }
                continue;
            }

            body.Append("export declare namespace ").Append(group.Key).AppendLine(" {");
            foreach (var declared in types)
            {
                // Members of an ambient namespace are exported implicitly.
                body.Append(Declare(declared, string.Empty, indent: "    "));
                if (declared != types[^1])
                {
                    body.AppendLine();
                }
            }
            body.AppendLine("}").AppendLine();
        }

        var builder = new StringBuilder();
        AppendHeader(builder);

        foreach (var alias in _globalAliases.Values.OrderBy(a => a, StringComparer.Ordinal))
        {
            // Emitted for a namespace-less type whose name is shadowed inside a namespace that
            // references it; without this the reference would silently bind to the wrong type.
            var target = _globalAliases.First(pair => pair.Value == alias).Key;
            builder.Append("type ").Append(alias).Append(" = ").Append(target).AppendLine(";");
        }

        if (_globalAliases.Count > 0)
        {
            builder.AppendLine();
        }

        builder.Append(body.ToString().TrimEnd()).AppendLine();

        return new GeneratedTypeScript([
            new GeneratedTypeScriptFile("index.d.ts", _options.ModuleSpecifier, builder.ToString())
        ]);
    }

    private string Declare(DeclaredType declared, string modifier, string indent)
    {
        _currentNamespace = declared.Namespace;

        var text = declared.Type.IsEnum ? DeclareEnum(declared, modifier) : DeclareInterface(declared, modifier);

        if (indent.Length == 0)
        {
            return text;
        }

        var lines = text.TrimEnd().Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.Length == 0 ? string.Empty : indent + line.TrimEnd('\r')).AppendLine();
        }
        return builder.ToString();
    }
    
    private DeclaredType? Collect(Type type)
    {
        if (_declared.TryGetValue(type, out var existing))
        {
            return existing;
        }

        if (!ShouldDeclare(type))
        {
            return null;
        }

        var declared = new DeclaredType(type, NamespaceFor(type), TypeScriptNameFor(type));

        // Registered before walking members so a recursive model terminates.
        _declared[type] = declared;

        if (!type.IsEnum)
        {
            foreach (var member in ReadableMembers(type).Where(m => !IsIgnored(m)))
            {
                CollectReferences(MemberTypeOf(member).Type);
            }
        }

        return declared;
    }

    private void CollectReferences(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            CollectReferences(underlying);
            return;
        }

        if (DictionaryValueType(type) is { } valueType)
        {
            CollectReferences(valueType);
            return;
        }

        if (EnumerableElementType(type) is { } elementType)
        {
            CollectReferences(elementType);
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CollectReferences(argument);
            }
            return;
        }

        Collect(type);
    }

    private static bool ShouldDeclare(Type type)
    {
        if (type.IsEnum)
        {
            return true;
        }

        if (IsIntrinsic(type) || type.IsPrimitive || type.IsPointer || type.IsByRef)
        {
            return false;
        }

        return type.IsClass || type.IsInterface || type is { IsValueType: true, IsPrimitive: false };
    }

    private static bool IsIntrinsic(Type type) =>
        type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(Version)
        || type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument)
        || type == typeof(byte[]) || IsNumeric(type) || type == typeof(bool);

    private string NamespaceFor(Type type)
    {
        if (!_options.MirrorNamespaces)
        {
            return string.Empty;
        }

        var @namespace = type.Namespace;
        if (string.IsNullOrEmpty(@namespace))
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(_options.TrimNamespacePrefix))
        {
            var prefix = _options.TrimNamespacePrefix.TrimEnd('.');
            if (@namespace == prefix)
            {
                return string.Empty;
            }
            if (@namespace.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                @namespace = @namespace[(prefix.Length + 1)..];
            }
        }

        return @namespace;
    }

    private void AppendHeader(StringBuilder builder)
    {
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("//     Generated by JsxCore from .NET types. Edits are overwritten on the next run.");
        builder.AppendLine("//");
        builder.Append("//     import type { SampleApp } from \"").Append(_options.ModuleSpecifier).AppendLine("\";");
        builder.AppendLine("//     ...then reference a type by its .NET namespace, e.g. SampleApp.Models.Product.");
        builder.AppendLine("//");
        builder.AppendLine("//     These describe the model as it arrives in JavaScript, so they follow the");
        builder.AppendLine("//     application's JsonSerializerOptions rather than the .NET shape directly.");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine();
    }

    private string DeclareEnum(DeclaredType declared, string modifier)
    {
        var type = declared.Type;
        var members = Enum.GetNames(type)
            .Select(memberName => EmitEnumsAsStrings(type)
                ? $"\"{memberName}\""
                : Convert.ToInt64(Enum.Parse(type, memberName)).ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        AppendSummary(builder, type);
        builder.Append(modifier).Append("type ").Append(declared.Name).Append(" =").AppendLine();
        builder.Append("    | ").Append(string.Join($"{Environment.NewLine}    | ", members)).AppendLine(";");
        return builder.ToString();
    }

    private bool EmitEnumsAsStrings(Type type)
    {
        if (_options.EnumsAsStrings is { } configured)
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

        return _json.Converters.Any(c => IsStringEnumConverter(c.GetType()));
    }

    private static bool IsStringEnumConverter(Type converter) =>
        converter == typeof(JsonStringEnumConverter)
        || (converter.IsGenericType && converter.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>))
        || typeof(JsonStringEnumConverter).IsAssignableFrom(converter);

    private string DeclareInterface(DeclaredType declared, string modifier)
    {
        var builder = new StringBuilder();
        AppendSummary(builder, declared.Type);
        builder.Append(modifier).Append("interface ").Append(declared.Name).AppendLine(" {");

        foreach (var member in ReadableMembers(declared.Type))
        {
            if (IsIgnored(member))
            {
                continue;
            }

            var (memberType, nullable) = MemberTypeOf(member);
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

    private static void AppendSummary(StringBuilder builder, Type type)
    {
        builder.Append("/** Generated from ").Append(type.FullName ?? type.Name).AppendLine(". */");
    }

    private IEnumerable<MemberInfo> ReadableMembers(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.MetadataToken))
        {
            yield return property;
        }

        if (!_options.IncludeFields && !_json.IncludeFields)
        {
            yield break;
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(f => f.MetadataToken))
        {
            yield return field;
        }
    }

    private static bool IsIgnored(MemberInfo member)
    {
        var ignore = member.GetCustomAttribute<JsonIgnoreAttribute>();
        return ignore is not null && ignore.Condition == JsonIgnoreCondition.Always;
    }

    private (Type Type, bool Nullable) MemberTypeOf(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
            {
                var info = _nullability.Create(property);
                var underlying = Nullable.GetUnderlyingType(property.PropertyType);
                return (underlying ?? property.PropertyType,
                    underlying is not null || info.ReadState == NullabilityState.Nullable);
            }
            case FieldInfo field:
            {
                var info = _nullability.Create(field);
                var underlying = Nullable.GetUnderlyingType(field.FieldType);
                return (underlying ?? field.FieldType,
                    underlying is not null || info.ReadState == NullabilityState.Nullable);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(member));
        }
    }

    private string JsonNameFor(MemberInfo member)
    {
        var explicitName = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        var name = explicitName ?? _json.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;

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

        if (IsNumeric(type))
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

        if (DictionaryValueType(type) is { } valueType)
        {
            return $"Record<string, {TypeReference(valueType)}>";
        }

        if (EnumerableElementType(type) is { } elementType)
        {
            var element = TypeReference(elementType);
            // Union members need parenthesising before the array suffix.
            return element.Contains('|', StringComparison.Ordinal) ? $"({element})[]" : $"{element}[]";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            var arguments = type.GetGenericArguments();
            return $"{{ key: {TypeReference(arguments[0])}; value: {TypeReference(arguments[1])} }}";
        }

        if (_declared.TryGetValue(type, out var declared))
        {
            return ReferenceName(declared);
        }

        return "unknown";
    }

    private string ReferenceName(DeclaredType declared)
    {
        if (declared.Namespace == _currentNamespace)
        {
            return declared.Name;
        }

        if (declared.Namespace.Length > 0)
        {
            return declared.QualifiedName;
        }

        // A namespace-less type referenced from inside a namespace resolves by simple name, unless
        // that namespace declares something of the same name, which would silently shadow it.
        var shadowed = _declared.Values.Any(other =>
            other.Namespace == _currentNamespace && other.Name == declared.Name);

        if (!shadowed)
        {
            return declared.Name;
        }

        if (!_globalAliases.TryGetValue(declared.Name, out var alias))
        {
            alias = declared.Name + "$Global";
            _globalAliases[declared.Name] = alias;
        }

        return alias;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
        || type == typeof(nint) || type == typeof(nuint)
        || type == typeof(Int128) || type == typeof(UInt128) || type == typeof(Half);

    private static Type? DictionaryValueType(Type type)
    {
        foreach (var candidate in Interfaces(type))
        {
            if (candidate.IsGenericType
                && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            {
                return candidate.GetGenericArguments()[1];
            }
        }

        return typeof(IDictionary).IsAssignableFrom(type) ? typeof(object) : null;
    }

    private static Type? EnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        foreach (var candidate in Interfaces(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return typeof(IEnumerable).IsAssignableFrom(type) ? typeof(object) : null;
    }

    private static IEnumerable<Type> Interfaces(Type type)
    {
        if (type.IsInterface)
        {
            yield return type;
        }

        foreach (var candidate in type.GetInterfaces())
        {
            yield return candidate;
        }
    }

    private static string TypeScriptNameFor(Type type)
    {
        var configured = type.GetCustomAttribute<JsxModelAttribute>()?.Name;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var name = type.Name;

        // Generic types arrive as "Paged`1"; flatten the arguments into the name so that
        // Paged<Order> and Paged<Customer> do not collide.
        if (type.IsGenericType)
        {
            var index = name.IndexOf('`', StringComparison.Ordinal);
            if (index >= 0)
            {
                name = name[..index];
            }
            name += string.Concat(type.GetGenericArguments().Select(TypeScriptNameFor));
        }

        // A nested type shares its namespace with its declaring type, so it needs the prefix to
        // stay unique within the module.
        if (type is { IsNested: true, DeclaringType: not null })
        {
            name = TypeScriptNameFor(type.DeclaringType) + name;
        }

        return name;
    }
}
