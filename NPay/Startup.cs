using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using AI;
using AI.Providers.Claude;
using NPay.Components;
using NPay.Core.Extensions;
using NPay.Options;
using NPay.Services;
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
builder.Services.Configure<SettlementAiSummaryOptions>(builder.Configuration.GetSection("AI:SettlementSummary"));
builder.Services.Configure<ClaudeChatClientOptions>(builder.Configuration.GetSection("Claude"));
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("Claude:ApiKey")))
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClaudeChatClientOptions>>();
        return new ClaudeChatClient(factory.CreateClient(), options);
    });
}
builder.Services.AddScoped<ISettlementAiSummaryService, SettlementAiSummaryService>();

// Register NPay business logic services.
var nPayConnectionString = builder.Configuration.GetConnectionString("NPay")
    ?? throw new InvalidOperationException("ConnectionStrings:NPay is not configured.");
builder.Services.AddNPay(nPayConnectionString);

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
