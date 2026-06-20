using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Payload for a full component tree snapshot (protocol v2 flat wire format).
/// </summary>
public sealed record ComponentTreeUpdatePayload
{
    /// <summary>All component nodes in the tree, linked by <see cref="ComponentNode.ParentId"/>.</summary>
    [JsonPropertyName("nodes")]
    public required IReadOnlyList<ComponentNode> Nodes { get; init; }
}
