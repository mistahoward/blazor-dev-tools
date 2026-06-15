using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// A single injected service on a component.
/// </summary>
public sealed record ComponentInjection
{
    /// <summary>Injection property name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Declared service type.</summary>
    [JsonPropertyName("serviceType")]
    public required string ServiceType { get; init; }

    /// <summary>Concrete implementation type, if resolved.</summary>
    [JsonPropertyName("implementationType")]
    public string? ImplementationType { get; init; }
}
