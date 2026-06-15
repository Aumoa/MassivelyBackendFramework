using AutomatedWiki.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenIDConnect.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
ValidateProductionAllowedHosts(builder);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(o => o.ResourcesPath = "Localizations");
ConfigureDataProtection(builder);
builder.Services.AddAuthorizationCore();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenIDConnect(builder.Configuration);

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] supportedCultures = ["en", "ko"];
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRouting();
app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

return;

static void ValidateProductionAllowedHosts(WebApplicationBuilder builder)
{
    if (builder.Environment.IsDevelopment())
    {
        return;
    }

    var allowedHosts = builder.Configuration["AllowedHosts"];
    var hosts = allowedHosts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (hosts is not { Length: > 0 } || hosts.Any(static host => host == "*"))
    {
        throw new InvalidOperationException("AllowedHosts must be configured with explicit host names outside Development.");
    }
}

static void ConfigureDataProtection(WebApplicationBuilder builder)
{
    var dataProtection = builder.Configuration.GetRequiredSection("DataProtection");
    var redisConnectionString = dataProtection.GetValue<string>("RedisConnectionString");
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException("DataProtection:RedisConnectionString is not configured.");
    }

    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
        .SetApplicationName("AutomatedWiki");
}
