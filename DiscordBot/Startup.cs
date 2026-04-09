using DiscordBot.Components;
using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using DiscordBot.SQL.Migration;
using Microsoft.Extensions.Options;
using SQLMigration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    await StartMigrationAsync(app.Lifetime.ApplicationStopping);
}

app.Run();

return;

void RegisterServices(IServiceCollection sc, IConfiguration conf)
{
    sc.AddHttpClient();

    sc.Configure<DiscordService.Configuration>(conf.GetRequiredSection("Discord"));
    sc.AddHostedService<DiscordService>();

    sc.Configure<OllamaService.Configuration>(conf.GetRequiredSection("Ollama"));
    sc.AddSingleton<OllamaService>();

    sc.Configure<MySqlOptions>(conf.GetRequiredSection("MySql"));
    sc.AddTransient<IChatLogRepository, MySqlChatLogRepository>();
}

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new ASPNETUtility.LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database,
        [.. scripts.GetScripts()], logger, cancellationToken);
}