using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OAuth2.Components;
using OAuth2.Core.Extensions;
using OAuth2.Options;
using OAuth2.SQL.Migration;
using OAuth2.Utility;
using SQLMigration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var bearer = builder.Configuration.GetSection("Bearer");
        options.Audience = bearer["Audience"];
    });
builder.Services.AddAuthorizationCore();

builder.Services.AddOAuth2(builder.Configuration.GetRequiredSection("OAuth2"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() == false)
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

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

await StartMigrationAsync(app.Lifetime.ApplicationStopping);

app.Run();

async ValueTask StartMigrationAsync(CancellationToken cancellationToken)
{
    var options = app.Services.GetRequiredService<IOptions<MySqlOptions>>();
    var scripts = new Scripts();
    var logger = new LoggerTextWriter(app.Logger);
    await Executor.RunAsync(options.Value.ConnectionString, options.Value.Database, [.. scripts.GetScripts()], logger, cancellationToken);
}