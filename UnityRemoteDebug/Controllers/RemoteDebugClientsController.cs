using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UnityRemoteDebug.Authorization;
using UnityRemoteDebug.Contracts;
using UnityRemoteDebug.Options;
using UnityRemoteDebug.Services;

namespace UnityRemoteDebug.Controllers;

[ApiController]
[Route("api/clients")]
public sealed class RemoteDebugClientsController(
    RemoteDebugClientRegistry clientRegistry,
    IOptions<RemoteDebugClientConnectionOptions> options,
    ILogger<RemoteDebugClientsController> logger) : ControllerBase
{
    private const string SharedSecretHeader = "X-UnityRemoteDebug-Secret";
    private const int ReceiveBufferSize = 4096;

    [HttpGet]
    [Authorize(Policy = RemoteDebugAuthorizationPolicies.Management)]
    public RemoteDebugClientListResponse ListClients()
    {
        return new RemoteDebugClientListResponse(clientRegistry.GetClients(), DateTimeOffset.UtcNow);
    }

    [HttpGet("connect")]
    public async Task<IActionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        var expectedSecret = options.Value.SharedSecret;
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unity RemoteDebug client connections are not configured.");
        }

        if (!TryAuthorizeClient(expectedSecret))
        {
            return Unauthorized();
        }

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return BadRequest("Unity RemoteDebug clients must connect with WebSocket.");
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var session = clientRegistry.Connect(new RemoteDebugClientRegistration(
            Request.Query["name"],
            Request.Query["project"],
            Request.Query["unityVersion"],
            Request.Query["platform"],
            HttpContext.Connection.RemoteIpAddress?.ToString()));

        logger.LogInformation("Unity RemoteDebug client connected. ClientId={ClientId}, DisplayName={DisplayName}.", session.ClientId, session.DisplayName);
        try
        {
            await ReceiveUntilClosedAsync(socket, session.ClientId, cancellationToken);
        }
        finally
        {
            clientRegistry.Disconnect(session.ClientId);
            logger.LogInformation("Unity RemoteDebug client disconnected. ClientId={ClientId}.", session.ClientId);
        }

        return new EmptyResult();
    }

    private async Task ReceiveUntilClosedAsync(WebSocket socket, Guid clientId, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(0, ReceiveBufferSize), cancellationToken);
                clientRegistry.Touch(clientId);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation(ex, "Unity RemoteDebug client WebSocket closed unexpectedly. ClientId={ClientId}.", clientId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool TryAuthorizeClient(string expectedSecret)
    {
        if (Request.Headers.TryGetValue(SharedSecretHeader, out var headerSecret) &&
            SecretEquals(headerSecret.ToString(), expectedSecret))
        {
            return true;
        }

        return false;
    }

    private static bool SecretEquals(string providedSecret, string expectedSecret)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
