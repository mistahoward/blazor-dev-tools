using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Payload when the user selects a component in the DevTools panel.
/// </summary>
public sealed record ComponentSelectionPayload
{
    /// <summary>Identifier of the selected component.</summary>
    [JsonPropertyName("componentId")]
    public required string ComponentId { get; init; }
}
