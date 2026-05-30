using MasterAdmin.Authorization;
using MasterAdmin.Components;
using MasterAdmin.Options;
using MasterAdmin.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenIDConnect.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(o => o.ResourcesPath = "Localizations");
builder.Services.Configure<MasterApiOptions>(builder.Configuration.GetRequiredSection("MasterApi"));
builder.Services.AddHttpClient<MasterOverviewClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<MasterApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.TimeoutMilliseconds));
});

var dataProtection = builder.Configuration.GetSection("DataProtection");
var keyPath = dataProtection.GetValue<string>("KeyPath");
if (!string.IsNullOrWhiteSpace(keyPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("MasterAdmin");
}

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy(MasterAuthorizationPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => MasterAuthorizationPolicies.HasAdminGroup(context.User));
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
