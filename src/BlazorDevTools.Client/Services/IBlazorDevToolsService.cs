using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

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
    /// Gets whether the Dev Tools JS bridge has been successfully initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the Dev Tools bridge using <paramref name="host"/> to reach the renderer,
    /// subscribes to navigation changes, and dispatches an initial component tree snapshot.
    /// Safe to call multiple times; the bridge initializes at most once per scope when JS interop is available.
    /// </summary>
    /// <param name="host">A live component used to reach the shared renderer.</param>
    /// <returns>A task that completes when initialization is attempted.</returns>
    Task InitializeAsync(ComponentBase host);

    /// <summary>
    /// Requests a debounced refresh of the component tree snapshot and dispatch to the extension.
    /// </summary>
    void RequestRefresh();
}
