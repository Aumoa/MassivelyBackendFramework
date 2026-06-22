using MasterAdmin.Authorization;
using MasterAdmin.Authentication;
using MasterAdmin.Components;
using MasterAdmin.Options;
using MasterAdmin.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using MasterServer.Services;
using OpenIDConnect.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(o => o.ResourcesPath = "Localizations");
builder.Services.Configure<MasterConnectionOptions>(builder.Configuration.GetRequiredSection("MasterConnection"));
builder.Services.AddSingleton<MasterOverviewSocketClient>();
builder.Services.AddSingleton<IMasterOverviewProvider>(static provider => provider.GetRequiredService<MasterOverviewSocketClient>());
builder.Services.AddSingleton<IServiceConnectionCredentials>(static provider => provider.GetRequiredService<MasterOverviewSocketClient>());
builder.Services.AddSingleton<IGatewayBackendRoutePolicy>(static provider => provider.GetRequiredService<MasterOverviewSocketClient>());
builder.Services.AddHostedService(static provider => provider.GetRequiredService<MasterOverviewSocketClient>());

var dataProtection = builder.Configuration.GetRequiredSection("DataProtection");
var redisConnectionString = dataProtection.GetValue<string>("RedisConnectionString");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("DataProtection:RedisConnectionString is not configured.");
}

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
    .SetApplicationName("MasterAdmin");

builder.Services.AddAuthentication(MasterAdminAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, MasterAdminAuthenticationHandler>(
        MasterAdminAuthenticationHandler.SchemeName,
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

app.UseRequestLocalization();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
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
    options.AddPolicy(MasterAuthorizationPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => MasterAuthorizationPolicies.HasAdminGroup(context.User));
    });
}
