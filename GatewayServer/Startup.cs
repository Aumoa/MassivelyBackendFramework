using ASPNETUtility;
using GatewayServer.Authorization;
using GatewayServer.Components;
using GatewayServer.Extensions;
using GatewayServer.Options;
using GatewayServer.SQL.Migration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenIDConnect.Extensions;
using SQLMigration;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(o => o.ResourcesPath = "Localizations");

builder.Services.AddGatewayServer(builder.Configuration);

var dataProtection = builder.Configuration.GetSection("DataProtection");
var keyPath = dataProtection.GetValue<string>("KeyPath");
if (!string.IsNullOrWhiteSpace(keyPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("GatewayServer");
}

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy(GatewayAuthorizationPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => GatewayAuthorizationPolicies.HasAdminGroup(context.User));
    });
});
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
    //await StartMigrationAsync(app.Lifetime.ApplicationStopping);
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
