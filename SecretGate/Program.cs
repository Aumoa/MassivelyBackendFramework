using ASPNETUtility;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenIDConnect.Extensions;
using SecretGate.Components;
using SecretGate.Options;
using SecretGate.Services;
using SecretGate.SQL.Migration;
using SQLMigration;
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
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] supportedCultures = ["en", "ko"];
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
});

ConfigureDataProtection(builder);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options => options.Audience = "secret-gate");
builder.Services.AddAuthorizationCore();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddOpenIDConnect(builder.Configuration);

builder.Services.Configure<MySqlOptions>(builder.Configuration.GetRequiredSection("MySql"));
builder.Services.AddScoped<SecretVaultSession>();
builder.Services.AddScoped<SecretVaultService>();
builder.Services.AddScoped<SecretShareService>();
builder.Services.AddScoped<ISecretRepository, MySqlSecretRepository>();
builder.Services.AddSingleton<SecretGateTokenGenerator>();

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

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    await StartMigrationAsync(app.Lifetime.ApplicationStopping);
}

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
        .SetApplicationName("SecretGate");
}

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database, [.. scripts.GetScripts()], logger, cancellationToken);
}
