using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// A node in the nested Blazor component tree.
/// </summary>
public sealed record ComponentNode
{
    /// <summary>Stable identifier for this component instance.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Display name of the component type.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Child components rendered by this component.</summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<ComponentNode> Children { get; init; } = [];

    /// <summary>
    /// Component parameters and cascading values. Omitted on the wire when empty.
    /// </summary>
    [JsonPropertyName("parameters")]
    public IReadOnlyList<ComponentParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Services injected into this component. Omitted on the wire when empty.
    /// </summary>
    [JsonPropertyName("injections")]
    public IReadOnlyList<ComponentInjection> Injections { get; init; } = [];

    /// <summary>
    /// Best-effort CSS selector for the component's first rendered element.
    /// Omitted on the wire when unavailable.
    /// </summary>
    [JsonPropertyName("locator")]
    public string? Locator { get; init; }
}
