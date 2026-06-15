using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// A single component parameter (route, query, cascading, etc.).
/// </summary>
public sealed record ComponentParameter
{
    /// <summary>Parameter property name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>CLR or declared type name.</summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>Serialized parameter value (any JSON value).</summary>
    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}
