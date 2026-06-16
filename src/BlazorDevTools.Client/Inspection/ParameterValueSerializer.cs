using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Safely serializes component parameter values into JSON elements suitable for the DevTools protocol.
/// </summary>
internal sealed class ParameterValueSerializer
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        MaxDepth = 4,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonElement _errorPlaceholder =
        JsonSerializer.SerializeToElement("<error>");

    /// <summary>
    /// Serializes a parameter value into a detached <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="value">The parameter value to serialize.</param>
    /// <returns>A safe JSON representation of the value.</returns>
    public JsonElement Serialize(object? value)
    {
        if (value is null)
            return JsonSerializer.SerializeToElement<object?>(null);

        try
        {
            return value switch
            {
                string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or decimal => JsonSerializer.SerializeToElement(value),
                Enum enumValue => JsonSerializer.SerializeToElement(enumValue.ToString()),
                RenderFragment => JsonSerializer.SerializeToElement("<RenderFragment>"),
                Type typeValue => JsonSerializer.SerializeToElement(typeValue.FullName ?? typeValue.Name),
                Delegate => JsonSerializer.SerializeToElement("<Delegate>"),
                _ when IsEventCallback(value.GetType()) => JsonSerializer.SerializeToElement("<EventCallback>"),
                _ => JsonSerializer.SerializeToElement(value, value.GetType(), _serializerOptions),
            };
        }
        catch (Exception)
        {
            try
            {
                return JsonSerializer.SerializeToElement(value.ToString());
            }
            catch (Exception)
            {
                return _errorPlaceholder;
            }
        }
    }

    /// <summary>
    /// Determines whether the specified <see cref="Type"/> represents an <see cref="EventCallback"/> or <see cref="EventCallback{T}"/>.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>
    /// <see langword="true"/> if the type is a generic <see cref="EventCallback{T}"/> or a non-generic <see cref="EventCallback"/>; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool IsEventCallback(Type type)
    {
        if (!type.IsGenericType)
            return false;

        Type genericDefinition = type.GetGenericTypeDefinition();
        return genericDefinition == typeof(EventCallback<>) || genericDefinition == typeof(EventCallback);
    }
}
