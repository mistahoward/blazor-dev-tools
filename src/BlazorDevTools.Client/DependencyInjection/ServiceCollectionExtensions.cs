using BlazorDevTools.Client.Inspection;
using BlazorDevTools.Client.Options;
using BlazorDevTools.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BlazorDevTools.Client.DependencyInjection;

/// <summary>
/// Dependency injection extensions for Blazor Dev Tools.
/// </summary>
public static class ServiceCollectionExtensions {
	/// <summary>
	/// Adds Blazor Dev Tools services to the specified <see cref="IServiceCollection"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configure">Optional callback to configure <see cref="BlazorDevToolsOptions"/>.</param>
	/// <returns>The same service collection for chaining.</returns>
	/// <remarks>
	/// <para>
	/// <see cref="IBlazorDevToolsService"/> is registered as scoped: one instance per Blazor circuit
	/// on Blazor Server and one per root scope on Blazor WebAssembly.
	/// </para>
	/// <para>
	/// By default, Dev Tools is enabled only when
	/// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.EnvironmentName"/> is
	/// <see cref="Microsoft.Extensions.Hosting.Environments.Development"/>.
	/// When the host environment is unavailable, Dev Tools stays disabled. Explicitly setting
	/// <see cref="BlazorDevToolsOptions.Enabled"/> to <see langword="true"/> must never be used in production.
	/// </para>
	/// </remarks>
	public static IServiceCollection AddBlazorDevTools(
		this IServiceCollection services,
		Action<BlazorDevToolsOptions>? configure = null) {
		services.AddOptions<BlazorDevToolsOptions>();
		services.Configure(configure ?? (_ => { }));
		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IConfigureOptions<BlazorDevToolsOptions>, ConfigureBlazorDevToolsOptions>());

		services.TryAddSingleton<ParameterValueSerializer>();
		services.TryAdd(ServiceDescriptor.Scoped<IComponentTreeInspector>(
			static sp => new ReflectionComponentTreeInspector(
				sp.GetRequiredService<ParameterValueSerializer>())));
		services.TryAdd(ServiceDescriptor.Scoped<IBlazorDevToolsService, BlazorDevToolsService>());
		return services;
	}
}
