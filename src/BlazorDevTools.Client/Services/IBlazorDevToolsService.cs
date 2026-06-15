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

    /// <summary>
    /// Gets whether the Dev Tools bridge has successfully dispatched its initial message.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the Dev Tools bridge and dispatches a mock component tree update
    /// to the browser extension. Safe to call multiple times; succeeds at most once
    /// per scope when JS interop is available.
    /// </summary>
    /// <returns>A task that completes when initialization is attempted.</returns>
    Task InitializeAsync();
}
