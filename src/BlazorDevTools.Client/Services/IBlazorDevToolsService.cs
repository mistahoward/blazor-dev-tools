namespace BlazorDevTools.Client.Services;

/// <summary>
/// Entry point for Blazor Dev Tools runtime integration in a consuming application.
/// </summary>
public interface IBlazorDevToolsService
{
    /// <summary>
    /// Gets whether Blazor Dev Tools integration is enabled for the current scope.
    /// </summary>
    bool IsEnabled { get; }
}
