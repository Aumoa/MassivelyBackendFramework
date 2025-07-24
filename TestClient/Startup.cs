using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Gateway.DTO;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Scripting.DTO;

CancellationTokenSource cancellationTokenSource = new();
var cancellationToken = cancellationTokenSource.Token;

while (true)
{
    string clientId = Guid.NewGuid().ToString();
    Console.WriteLine("ClientId: {0}", clientId);
    Console.WriteLine("If you want to start login, press any key to continue.");
    Console.ReadKey();

    var connection = new HubConnectionBuilder()
        .WithUrl($"https://localhost:7268/hub/gateway?client_id={Uri.EscapeDataString(clientId)}")
        .AddMessagePackProtocol()
        .Build();

    TaskCompletionSource<LoginResponseNotify> loginResponseTaskSource = new();
    connection.On<LoginResponseNotify>("LoginResponse", loginResponseTaskSource.SetResult);

    await connection.StartAsync(cancellationToken);
    Console.WriteLine("Client connected: {0}", connection.ConnectionId);

    string url = $"https://localhost:7268/api/auth/login/google?client_id={Uri.EscapeDataString(clientId)}";
    Console.WriteLine("Redirecting to {0}...", url);
    Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });

    Console.WriteLine("Wait for login response...");
    var loginResponseNotify = await loginResponseTaskSource.Task;
    if (loginResponseNotify.Code != ResponseCode.Success)
    {
        Console.WriteLine("Login failed: {0}", loginResponseNotify.Code);
        continue;
    }

    var jwt = loginResponseNotify.AccessJwt;
    var handler = new JwtSecurityTokenHandler();
    var token = handler.ReadJwtToken(jwt);

    string? name = token.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
    string? email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

    Console.WriteLine("Succeeded to logged in.");
    Console.WriteLine("Name: {0}", name);
    Console.WriteLine("Email: {0}", email);

    Console.ReadKey();
    Console.WriteLine("Stopping connection...");
    await connection.StopAsync(cancellationToken);
    Console.WriteLine("Connection stopped. Press any key to start again or Ctrl+C to exit.");
    await connection.StopAsync(cancellationToken);
}
