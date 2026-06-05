using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenIDConnect.Extensions;
using UnityRemoteDebug.Authorization;
using UnityRemoteDebug.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(options => options.ResourcesPath = "Localizations");

var dataProtectionApplicationName = builder.Configuration.GetValue<string>("DataProtection:ApplicationName");
builder.Services.AddDataProtection()
    .SetApplicationName(string.IsNullOrWhiteSpace(dataProtectionApplicationName)
        ? "UnityRemoteDebug"
        : dataProtectionApplicationName);

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy(RemoteDebugAuthorizationPolicies.Management, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => RemoteDebugAuthorizationPolicies.HasManagementGroup(context.User));
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
app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
