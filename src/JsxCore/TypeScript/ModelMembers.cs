using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsxCore.TypeScript;

/// <summary>
/// Which members of a model are described, and what type each one has once nullability is taken
/// into account.
/// </summary>
/// <remarks>
/// Both halves of generation ask this: collection walks member types to find what else has to be
/// declared, and emission writes them out. They must agree exactly, or a type is referenced that
/// was never declared.
/// </remarks>
internal sealed class ModelMembers(TypeDefinitionOptions options, JsonSerializerOptions json)
{
    private readonly NullabilityInfoContext _nullability = new();

    public IEnumerable<MemberInfo> Readable(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.MetadataToken))
        {
            yield return property;
        }

        if (!options.IncludeFields && !json.IncludeFields)
        {
            yield break;
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(f => f.MetadataToken))
        {
            yield return field;
        }
    }

    /// <summary>Members that are described, which is the readable ones minus the ignored ones.</summary>
    public IEnumerable<MemberInfo> Described(Type type) => Readable(type).Where(member => !IsIgnored(member));

    public static bool IsIgnored(MemberInfo member)
    {
        var ignore = member.GetCustomAttribute<JsonIgnoreAttribute>();
        return ignore is not null && ignore.Condition == JsonIgnoreCondition.Always;
    }

    public (Type Type, bool Nullable) TypeOf(MemberInfo member)
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
}
