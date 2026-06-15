using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Envelope wrapping all domain messages exchanged between the extension and Blazor client.
/// </summary>
/// <typeparam name="TPayload">Type-specific payload body.</typeparam>
public sealed record DevToolsEnvelope<TPayload>
{
    /// <summary>Protocol identifier; must be <see cref="DevToolsProtocol.Name"/>.</summary>
    [JsonPropertyName("protocol")]
    public required string Protocol { get; init; }

    /// <summary>Protocol version; must match <see cref="DevToolsProtocol.Version"/> for this schema.</summary>
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    /// <summary>Message discriminator.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Type-specific payload body.</summary>
    [JsonPropertyName("payload")]
    public required TPayload Payload { get; init; }
}
