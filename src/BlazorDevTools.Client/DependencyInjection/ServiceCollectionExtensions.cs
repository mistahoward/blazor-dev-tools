using BlazorDevTools.Client.Inspection;
using BlazorDevTools.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorDevTools.Client.DependencyInjection;

/// <summary>
/// Dependency injection extensions for Blazor Dev Tools.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Blazor Dev Tools services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// <see cref="IBlazorDevToolsService"/> is registered as scoped: one instance per Blazor circuit
    /// on Blazor Server and one per root scope on Blazor WebAssembly.
    /// </remarks>
    public static IServiceCollection AddBlazorDevTools(this IServiceCollection services)
    {
        services.TryAddSingleton<BlazorInternalsAccessor>();
        services.TryAddSingleton<ParameterValueSerializer>();
        services.TryAddScoped<IComponentTreeInspector, ReflectionComponentTreeInspector>();
        services.TryAddScoped<IBlazorDevToolsService, BlazorDevToolsService>();
        return services;
    }
}
