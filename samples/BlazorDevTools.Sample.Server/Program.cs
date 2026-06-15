using BlazorDevTools.Client.DependencyInjection;
using BlazorDevTools.Sample.Server.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddBlazorDevTools();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
#if NET10_0_OR_GREATER
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
#else
    app.UseExceptionHandler("/Error");
#endif
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
#if NET10_0_OR_GREATER
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
#else
app.UseStatusCodePagesWithReExecute("/not-found");
#endif
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
