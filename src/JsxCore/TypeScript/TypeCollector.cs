using System.Reflection;

namespace JsxCore.TypeScript;

/// <summary>
/// Works out which .NET types have to be declared, and under what name. Writes no TypeScript.
/// </summary>
internal sealed class TypeCollector(TypeDefinitionOptions options, ModelMembers members)
{
    private readonly Dictionary<Type, DeclaredType> _declared = [];

    /// <summary>Everything that will be declared, keyed by the .NET type it came from.</summary>
    public IReadOnlyDictionary<Type, DeclaredType> Collect()
    {
        _declared.Clear();

        // Nothing is named after an assembly any more, so an assembly called "globals" or
        // "rendering" can no longer shadow a reserved module. The guard that used to be here went
        // with the naming it protected.
        foreach (var type in options.ResolveTypes())
        {
            Declare(type);
        }

        CollectGlobals();

        return _declared;
    }

    /// <summary>
    /// A type reachable only through a registered global still has to be declared, because the
    /// globals module references it.
    /// </summary>
    private void CollectGlobals()
    {
        foreach (var type in options.GlobalTypes.Values.Where(type => type is not null))
        {
            foreach (var method in TypeShape.CallableMethods(type!))
            {
                foreach (var parameter in method.GetParameters())
                {
                    Reference(parameter.ParameterType);
                }

                if (method.ReturnType != typeof(void))
                {
                    Reference(method.ReturnType);
                }
            }

            foreach (var property in TypeShape.ReadableProperties(type!))
            {
                Reference(property.PropertyType);
            }
        }
    }

    private DeclaredType? Declare(Type type)
    {
        if (_declared.TryGetValue(type, out var existing))
        {
            return existing;
        }

        if (!TypeShape.ShouldDeclare(type))
        {
            return null;
        }

        var declared = new DeclaredType(type, NamespaceFor(type), TypeScriptNameFor(type));

        // Registered before walking members so a recursive model terminates.
        _declared[type] = declared;

        if (!type.IsEnum)
        {
            foreach (var member in members.Described(type))
            {
                Reference(members.TypeOf(member).Type);
            }
        }

        return declared;
    }

    /// <summary>
    /// Declares whatever a reference to this type ends up naming, which for a collection or a
    /// dictionary is what it holds rather than the container.
    /// </summary>
    private void Reference(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            Reference(underlying);
            return;
        }

        if (TypeShape.DictionaryValueType(type) is { } valueType)
        {
            Reference(valueType);
            return;
        }

        if (TypeShape.EnumerableElementType(type) is { } elementType)
        {
            Reference(elementType);
            return;
        }

        if (TypeShape.IsKeyValuePair(type))
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Reference(argument);
            }
            return;
        }

        Declare(type);
    }

    private string NamespaceFor(Type type)
    {
        if (!options.MirrorNamespaces)
        {
            return string.Empty;
        }

        var @namespace = type.Namespace;
        if (string.IsNullOrEmpty(@namespace))
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(options.TrimNamespacePrefix))
        {
            var prefix = options.TrimNamespacePrefix.TrimEnd('.');
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
