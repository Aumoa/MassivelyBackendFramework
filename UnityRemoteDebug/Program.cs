using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenIDConnect.Extensions;
using StackExchange.Redis;
using UnityRemoteDebug.Authentication;
using UnityRemoteDebug.Authorization;
using UnityRemoteDebug.Components;
using UnityRemoteDebug.Options;
using UnityRemoteDebug.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(options => options.ResourcesPath = "Localizations");
builder.Services.Configure<RemoteDebugClientConnectionOptions>(builder.Configuration.GetSection("ClientConnection"));
builder.Services.AddSingleton<RemoteDebugClientRegistry>();

var dataProtection = builder.Configuration.GetRequiredSection("DataProtection");
var redisConnectionString = dataProtection.GetValue<string>("RedisConnectionString");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("DataProtection:RedisConnectionString is not configured.");
}

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
    .SetApplicationName("UnityRemoteDebug");

builder.Services.AddAuthentication(RemoteDebugAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, RemoteDebugAuthenticationHandler>(
        RemoteDebugAuthenticationHandler.SchemeName,
        options => { });
builder.Services.AddAuthorization(ConfigureAuthorization);
builder.Services.AddAuthorizationCore(ConfigureAuthorization);
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
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseRequestLocalization();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseWebSockets();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static void ConfigureAuthorization(AuthorizationOptions options)
{
    options.AddPolicy(RemoteDebugAuthorizationPolicies.Management, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => RemoteDebugAuthorizationPolicies.HasManagementGroup(context.User));
    });
}
