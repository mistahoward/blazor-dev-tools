namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// Protocol identifier constants for domain DevTools messages.
/// </summary>
public static class DevToolsProtocol
{
    /// <summary>Protocol name sent on every domain message.</summary>
    public const string Name = "blazor-devtools";

    /// <summary>Current protocol version.</summary>
    public const int Version = 2;
}
