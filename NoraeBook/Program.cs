using ASPNETUtility;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using NoraeBook.Components;
using NoraeBook.Options;
using NoraeBook.Repositories;
using NoraeBook.Services;
using NoraeBook.SQL.Migration;
using OpenIDConnect.Extensions;
using SQLMigration;

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
builder.Services.Configure<MySqlOptions>(builder.Configuration.GetRequiredSection("MySql"));
builder.Services.AddScoped<IKaraokeSongRepository, MySqlKaraokeSongRepository>();

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
    var keyPath = builder.Configuration.GetValue<string>("DataProtection:KeyPath");
    if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(keyPath))
    {
        keyPath = ".data-protection-keys";
    }

    if (string.IsNullOrWhiteSpace(keyPath))
    {
        return;
    }

    var resolvedKeyPath = Path.IsPathFullyQualified(keyPath)
        ? keyPath
        : Path.Combine(builder.Environment.ContentRootPath, keyPath);

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(resolvedKeyPath))
        .SetApplicationName("NoraeBook");
}

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database, [.. scripts.GetScripts()], logger, cancellationToken);
}
