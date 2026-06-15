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
}
