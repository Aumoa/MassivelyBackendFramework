using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UnityRemoteDebug.Backend.Extensions;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.WebHost.UseUrls(builder.Configuration.GetValue<string>("HealthEndpoint:Url") ?? "http://0.0.0.0:11808");
builder.Services.AddUnityRemoteDebugBackend(builder.Configuration);

var app = builder.Build();
app.MapGet("/healthz", static () => "OK");
await app.RunAsync();
