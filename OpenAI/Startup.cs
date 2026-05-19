using ASPNETUtility;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenAI.Components;
using OpenAI.Extensions;
using OpenAI.Options;
using OpenAI.Services;
using OpenAI.SQL.Migration;
using OpenIDConnect;
using OpenIDConnect.Extensions;
using SQLMigration;

var builder = WebApplication.CreateBuilder(args);
WebApplication? app = null;

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(o => o.ResourcesPath = "Localizations");

builder.Services.AddClaudeAI(builder.Configuration);
builder.Services.AddScoped<ChatStateService>();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] supportedCultures = ["en", "ko"];
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
});

var dataProtection = builder.Configuration.GetSection("DataProtection");
if (dataProtection.Exists())
{
    var keyPath = dataProtection.GetValue<string>("KeyPath")
        ?? throw new InvalidOperationException("DataProtection:KeyPath is not configured.");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("OpenAI");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenIDConnect(builder.Configuration);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var oidcOptions = builder.Configuration.GetRequiredSection("OIDC").Get<OIDCOptions>()
            ?? throw new InvalidOperationException("OIDC section is not properly configured.");
        options.Audience = oidcOptions.ClientId;
        options.Authority = oidcOptions.Uri;
        options.RequireHttpsMetadata = true;

        options.Events = new()
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Token is null && ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    string? idToken = authHeader;
                    if (!string.IsNullOrEmpty(idToken) && idToken.StartsWith("Bearer "))
                    {
                        idToken = idToken["Bearer".Length..].Trim();
                        ctx.Token = idToken;
                    }
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = ctx =>
            {
                var l = app?.Logger;
                if (l?.IsEnabled(LogLevel.Information) == true)
                {
                    l.LogInformation("Authentication failed: {Message}", ctx.Exception.Message);
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();
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

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database, [.. scripts.GetScripts()], logger, cancellationToken);
}
