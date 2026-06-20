using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for DevTools protocol serialization.
/// </summary>
public static class DevToolsJsonSerializerOptions
{
    /// <summary>
    /// Options for serializing domain envelopes dispatched to the browser.
    /// </summary>
    public static JsonSerializerOptions Envelope { get; } = new()
    {
        MaxDepth = 256,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Options for hashing protocol payloads deterministically.
    /// </summary>
    public static JsonSerializerOptions PayloadHash { get; } = new()
    {
        MaxDepth = 256,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
