using AI;
using AI.Providers.Claude;
using DiscordBot.Components;
using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using DiscordBot.Services.ImageGeneration;
using DiscordBot.SQL.Migration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenIDConnect.Extensions;
using SQLMigration;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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
    .SetApplicationName("DiscordBot");

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options => options.Audience = "discordbot");
builder.Services.AddAuthorizationCore();

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenIDConnect(builder.Configuration);

RegisterServices(builder.Services, builder.Configuration);

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

await InitializeClaudeSettingsAsync(app.Lifetime.ApplicationStopping);

app.Run();

return;

void RegisterServices(IServiceCollection sc, IConfiguration conf)
{
    sc.AddHttpClient();
    sc.AddMemoryCache();

    sc.Configure<DiscordService.Configuration>(conf.GetRequiredSection("Discord"));
    sc.AddHostedService<DiscordService>();

    sc.Configure<ImageGenerationOptions>(conf.GetRequiredSection("ImageGeneration"));
    sc.AddSingleton<ImagePromptProfileProvider>();
    sc.AddSingleton<IChatLogImageProcessor, ChatLogImageProcessor>();
    sc.AddHttpClient<IImageGenerationClient, ComfyUIClient>((sp, client) =>
    {
        var imageOptions = sp.GetRequiredService<IOptions<ImageGenerationOptions>>().Value;
        client.BaseAddress = new Uri(imageOptions.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(30, imageOptions.TimeoutSeconds + 30));
    });

    sc.Configure<ClaudeChatClientOptions>(conf.GetRequiredSection("Claude"));
    sc.AddSingleton<IChatClient>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var options = sp.GetRequiredService<IOptions<ClaudeChatClientOptions>>();
        return new ClaudeChatClient(factory.CreateClient(), options);
    });

    sc.Configure<OllamaService.Configuration>(conf.GetRequiredSection("Claude"));
    sc.AddSingleton<OllamaService>();

    sc.Configure<MySqlOptions>(conf.GetRequiredSection("MySql"));
    sc.AddTransient<IChatLogRepository, MySqlChatLogRepository>();
    sc.AddTransient<IAllowedChannelRepository, MySqlAllowedChannelRepository>();
    sc.AddScoped<IAllowedChannelService, AllowedChannelService>();
    sc.AddTransient<IToolSettingsRepository, MySqlToolSettingsRepository>();
    sc.AddScoped<IToolSettingsService, ToolSettingsService>();
    sc.AddSingleton<IClaudeSettingsRepository, MySqlClaudeSettingsRepository>();
    sc.AddSingleton<IClaudeSettingsService, ClaudeSettingsService>();
}

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new ASPNETUtility.LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database,
        [.. scripts.GetScripts()], logger, cancellationToken);
}

async ValueTask InitializeClaudeSettingsAsync(CancellationToken cancellationToken)
{
    using var scope = app.Services.CreateScope();
    var settings = scope.ServiceProvider.GetRequiredService<IClaudeSettingsService>();
    await settings.GetAsync(cancellationToken);
}
