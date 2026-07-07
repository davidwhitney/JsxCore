using System.Text.Json.Serialization;

namespace JsxCore.Rendering;

public sealed class HeadDescriptor
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("meta")]
    public List<Dictionary<string, string>>? Meta { get; set; }

    [JsonPropertyName("links")]
    public List<Dictionary<string, string>>? Links { get; set; }

    [JsonPropertyName("scripts")]
    public List<Dictionary<string, string>>? Scripts { get; set; }
}

public sealed record ServerRenderResult(string Html, HeadDescriptor? Head);

public sealed class DocumentContext
{
    public required string ViewName { get; init; }

    public required RenderMode RenderMode { get; init; }

    /// <summary>Server-rendered markup, or null when rendering on the client only.</summary>
    public string? ServerHtml { get; init; }

    public HeadDescriptor? Head { get; init; }

    /// <summary>The model, already serialised to JSON.</summary>
    public required string ModelJson { get; init; }

    /// <summary>The ambient view context, already serialised to JSON.</summary>
    public required string ContextJson { get; init; }

    public required string ModuleUrl { get; init; }

    public required string ClientSpecifier { get; init; }

    /// <summary>Import map entries to emit, mapping bare specifiers to URLs.</summary>
    public required IReadOnlyDictionary<string, string> ImportMap { get; init; }

    /// <summary>True when the hot reload client should be injected.</summary>
    public bool HotReloadEnabled { get; init; }

    public string? HotReloadClientUrl { get; init; }

    public string? HotReloadEndpoint { get; init; }

    public required JsxCoreOptions Options { get; init; }

    /// <summary>
    /// Document settings for this response: the configured defaults with any per-result overrides
    /// already applied.
    /// </summary>
    public required DocumentOptions Document { get; init; }

    /// <summary>
    /// Title explicitly set on the result. When present it wins over the view's head export,
    /// because it was chosen at the call site for this specific response.
    /// </summary>
    public string? TitleOverride { get; init; }
}
