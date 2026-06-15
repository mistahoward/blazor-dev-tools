namespace BlazorDevTools.Client.Protocol;

/// <summary>
/// String discriminators for domain DevTools protocol messages.
/// Values must match <c>MessageType</c> in the extension protocol contract.
/// </summary>
public static class DevToolsMessageType
{
    /// <summary>Full component tree snapshot.</summary>
    public const string ComponentTreeUpdate = "componentTreeUpdate";

    /// <summary>User selected a component in the DevTools panel.</summary>
    public const string ComponentSelection = "componentSelection";

    /// <summary>Parameters and injections for a specific component.</summary>
    public const string ComponentPropsUpdate = "componentPropsUpdate";
}
