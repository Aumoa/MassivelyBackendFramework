using Auth.Extensions;
using Gateway.Extensions;
using Master.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSignalR()
    .AddMessagePackProtocol();

builder.Services.AddMasterService();
builder.Services.AddGatewayService(builder.Configuration.GetSection("Gateway"));
builder.Services.AddAuthService(builder.Configuration.GetSection("Auth"));

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Integrated Service API",
        Version = "v1"
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

app.MapControllers();

app.MapMasterEndpoints();
app.MapGatewayEndpoints();
app.MapAuthEndpoints();

app.Run();
