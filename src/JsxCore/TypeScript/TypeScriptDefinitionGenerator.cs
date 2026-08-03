using System.Text.Json;

namespace JsxCore.TypeScript;

/// <summary>
/// Generates the TypeScript declarations for an application's .NET model types and globals.
/// </summary>
/// <remarks>
/// Two phases, in two classes. <see cref="TypeCollector"/> decides what has to be declared, walking
/// the model graph until it closes; <see cref="TypeScriptEmitter"/> writes it out. They are
/// separate because they used to be interleaved, and emission adding to the set it was midway
/// through printing meant a type could be referenced after the file describing it had been written.
/// </remarks>
public sealed class TypeScriptDefinitionGenerator(TypeDefinitionOptions options, JsonSerializerOptions json)
{
    private readonly TypeDefinitionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly JsonSerializerOptions _json = json ?? throw new ArgumentNullException(nameof(json));

    public GeneratedTypeScript Generate()
    {
        var members = new ModelMembers(_options, _json);
        var declared = new TypeCollector(_options, members).Collect();

        return new TypeScriptEmitter(_options, _json, members, declared).Emit();
    }
}
