using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using MinecraftSidecar.Components;
using MinecraftSidecar.Options;
using MinecraftSidecar.Services;
using OpenIDConnect.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("ko-KR") };
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
    .SetApplicationName("OAuth2");

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options => options.Audience = "minecraft-sidecar");
builder.Services.AddAuthorizationCore();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddOpenIDConnect(builder.Configuration);

builder.Services.Configure<RCONOptions>(builder.Configuration.GetRequiredSection("RCON"));
builder.Services.AddSingleton<RCONClient>();
builder.Services.AddHostedService(p => p.GetRequiredService<RCONClient>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() == false)
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
