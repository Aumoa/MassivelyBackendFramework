using Microsoft.AspNetCore.Components.Authorization;
using OAuth2.Components;
using OAuth2.Options;
using OAuth2.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("Origin", client =>
{
    client.BaseAddress = new Uri(GetRequiredValue<string>(builder.Configuration.GetRequiredSection("Blazor"), "Origin"));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(p => p.GetRequiredService<AuthProvider>());
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var bearer = builder.Configuration.GetSection("Bearer");
        options.Audience = bearer["Audience"];
    });
builder.Services.AddAuthorizationCore();

builder.Services.Configure<MySqlAccountsAndAccesses.Configuration>(builder.Configuration.GetRequiredSection("Accesses"));
builder.Services.AddScoped<PasswordHash>();
builder.Services.AddScoped<IAccounts, MySqlAccountsAndAccesses>();
builder.Services.AddScoped<IAccesses, MySqlAccountsAndAccesses>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetRequiredSection("JwtOptions"));
builder.Services.AddScoped<JwtTokenGenerator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() == false)
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

T GetRequiredValue<T>(IConfiguration config, string key)
{
    var value = config.GetValue<T>(key);
    if (EqualityComparer<T>.Default.Equals(value, default!))
    {
        throw new InvalidOperationException($"Configuration value for '{key}' is required.");
    }

    return value;
}