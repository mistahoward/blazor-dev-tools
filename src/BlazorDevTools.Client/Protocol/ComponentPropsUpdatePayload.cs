using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Payload with parameters and injections for a specific component.
/// </summary>
public sealed record ComponentPropsUpdatePayload
{
    /// <summary>Identifier of the component whose props are described.</summary>
    [JsonPropertyName("componentId")]
    public required string ComponentId { get; init; }

    /// <summary>Component parameters and their values.</summary>
    [JsonPropertyName("parameters")]
    public IReadOnlyList<ComponentParameter> Parameters { get; init; } = [];

    /// <summary>Services injected into the component.</summary>
    [JsonPropertyName("injections")]
    public IReadOnlyList<ComponentInjection> Injections { get; init; } = [];
}
