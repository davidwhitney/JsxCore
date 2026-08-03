namespace JsxCore.TypeScript;

/// <summary>
/// A .NET type that will be declared in TypeScript, and the name it will be declared under.
/// </summary>
/// <param name="Namespace">
/// The .NET namespace it is mirrored into, or empty for a type declared at the top level of the
/// module.
/// </param>
internal sealed record DeclaredType(Type Type, string Namespace, string Name)
{
    public string QualifiedName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
}
