using JenkinsFailureAnalyzer.Authentication;
using JenkinsFailureAnalyzer.Authorization;
using JenkinsFailureAnalyzer.Components;
using JenkinsFailureAnalyzer.Options;
using JenkinsFailureAnalyzer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenIDConnect.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(options => options.ResourcesPath = "Localizations");

builder.Services.Configure<FailureAnalysisIngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<FailureAnalysisStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IIngestionSecretValidator, ConfiguredIngestionSecretValidator>();
builder.Services.AddSingleton<IFailureAnalysisService, RuleBasedFailureAnalysisService>();
builder.Services.AddSingleton<IFailureAnalysisStore, FileFailureAnalysisStore>();

var dataProtection = builder.Configuration.GetRequiredSection("DataProtection");
var redisConnectionString = dataProtection.GetValue<string>("RedisConnectionString");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("DataProtection:RedisConnectionString is not configured.");
}

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
    .SetApplicationName("JenkinsFailureAnalyzer");

builder.Services.AddAuthentication(JenkinsFailureAnalyzerAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, JenkinsFailureAnalyzerAuthenticationHandler>(
        JenkinsFailureAnalyzerAuthenticationHandler.SchemeName,
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
    options.AddPolicy(JenkinsFailureAnalyzerAuthorizationPolicies.Management, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => JenkinsFailureAnalyzerAuthorizationPolicies.HasManagementGroup(context.User));
    });
}
