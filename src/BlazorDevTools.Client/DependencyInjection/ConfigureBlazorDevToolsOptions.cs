using BlazorDevTools.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BlazorDevTools.Client.DependencyInjection;

/// <summary>
/// Resolves <see cref="BlazorDevToolsOptions.Enabled"/> from the host environment when not explicitly set.
/// </summary>
internal sealed class ConfigureBlazorDevToolsOptions(IServiceProvider serviceProvider)
    : IConfigureOptions<BlazorDevToolsOptions>
{
    /// <inheritdoc />
    public void Configure(BlazorDevToolsOptions options)
    {
        options.Enabled ??= serviceProvider.GetService<IHostEnvironment>()?.IsDevelopment() ?? false;
    }
}
