using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using NPay.Components;
using NPay.Core.Extensions;
using OpenIDConnect.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

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

var dataProtection = builder.Configuration.GetRequiredSection("DataProtection");
var redisConnectionString = dataProtection.GetValue<string>("RedisConnectionString");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("DataProtection:RedisConnectionString is not configured.");
}

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
    .SetApplicationName("NPay");

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options => options.Audience = "npay");
builder.Services.AddAuthorizationCore();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddOpenIDConnect(builder.Configuration);

// Register NPay business logic services.
builder.Services.AddNPay();

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
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
