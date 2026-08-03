using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace JsxCore.TypeScript;

/// <summary>
/// What a .NET type is, as far as generating TypeScript is concerned.
/// </summary>
/// <remarks>
/// Shared by the two halves of generation, which have to agree: collection walks a type only when
/// it will be declared, and emission writes a reference only for the shape collection recognised.
/// The questions being asked in one place is what keeps them from drifting.
/// </remarks>
internal static class TypeShape
{
    /// <summary>Whether a type is worth declaring, rather than being written inline.</summary>
    public static bool ShouldDeclare(Type type)
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

    /// <summary>A type TypeScript already has a word for, so nothing has to be generated.</summary>
    public static bool IsIntrinsic(Type type) =>
        type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(Version)
        || type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument)
        || type == typeof(byte[]) || IsNumeric(type) || type == typeof(bool);

    public static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
        || type == typeof(nint) || type == typeof(nuint)
        || type == typeof(Int128) || type == typeof(UInt128) || type == typeof(Half);

    public static Type? DictionaryValueType(Type type)
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

    public static Type? EnumerableElementType(Type type)
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

    public static bool IsKeyValuePair(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

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

    /// <summary>
    /// What a view can call on a global: its own public instance methods, minus the plumbing every
    /// object has and the accessors that belong to properties.
    /// </summary>
    public static IEnumerable<MethodInfo> CallableMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName && method.DeclaringType != typeof(object))
            .Where(method => !method.IsGenericMethodDefinition)
            .OrderBy(method => method.Name, StringComparer.Ordinal);

    /// <summary>Public readable instance properties, which is what a global exposes as data.</summary>
    public static IEnumerable<PropertyInfo> ReadableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
