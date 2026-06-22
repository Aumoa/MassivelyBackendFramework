using ASPNETUtility;
using MasterServer.Extensions;
using MasterServer.Options;
using MasterServer.SQL.Migration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SQLMigration;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.WebHost.UseUrls(builder.Configuration.GetValue<string>("HealthEndpoint:Url") ?? "http://0.0.0.0:11801");
builder.Services.AddMasterServer(builder.Configuration);

var app = builder.Build();
app.MapGet("/healthz", static () => "OK");

if (app.Environment.IsDevelopment())
{
    await StartMigrationAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

return;

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<ServiceConnectionCredentialOptions>>();
    var scripts = new Scripts();
    var logger = new LoggerTextWriter(app.Logger);
    await Executor.RunAsync(
        options.Value.ConnectionString,
        options.Value.Database,
        [.. scripts.GetScripts()],
        logger,
        cancellationToken);
}
