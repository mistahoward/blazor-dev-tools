namespace BlazorDevTools.Client.Options;

/// <summary>
/// Configuration for Blazor Dev Tools runtime integration.
/// </summary>
public sealed class BlazorDevToolsOptions
{
    /// <summary>
    /// Gets or sets whether Blazor Dev Tools is enabled for the current application.
    /// When <see langword="null"/>, enablement is resolved from
    /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.IsDevelopment()"/>.
    /// When <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/> is unavailable, the default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Dev Tools exposes component trees, serialized parameters, and dependency-injection metadata to the
    /// browser via same-origin <c>window.postMessage</c>. Do not set this to <see langword="true"/> in
    /// production environments.
    /// </remarks>
    public bool? Enabled { get; set; }
}
