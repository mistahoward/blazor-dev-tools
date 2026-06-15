using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Payload for a full component tree snapshot.
/// </summary>
public sealed record ComponentTreeUpdatePayload
{
    /// <summary>Root of the component tree.</summary>
    [JsonPropertyName("root")]
    public required ComponentNode Root { get; init; }
}
