using System.Buffers;
using System.Buffers.Binary;
using System.IO;
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
    RemoteDebugClientChallengeStore challengeStore,
    IOptions<RemoteDebugClientConnectionOptions> options,
    ILogger<RemoteDebugClientsController> logger) : ControllerBase
{
    private const string ChallengeIdHeader = "X-UnityRemoteDebug-Challenge-Id";
    private const string ProofHeader = "X-UnityRemoteDebug-Proof";
    private const string ProofAlgorithm = "HMAC-SHA256/Base64Url";
    private const int ProofLength = 32;
    private const int ReceiveBufferSize = 4096;

    [HttpGet]
    [Authorize(Policy = RemoteDebugAuthorizationPolicies.Management)]
    public RemoteDebugClientListResponse ListClients()
    {
        return new RemoteDebugClientListResponse(clientRegistry.GetClients(), DateTimeOffset.UtcNow);
    }

    [HttpGet("challenge")]
    public IActionResult CreateChallenge()
    {
        if (string.IsNullOrWhiteSpace(options.Value.SharedSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unity RemoteDebug client connections are not configured.");
        }

        var challenge = challengeStore.Create(options.Value.ChallengeLifetime);
        return Ok(new RemoteDebugClientChallengeResponse(
            challenge.ChallengeId,
            Base64UrlEncode(challenge.Nonce),
            challenge.ExpiresAt,
            ProofAlgorithm));
    }

    [HttpGet("connect")]
    public async Task<IActionResult> ConnectAsync(CancellationToken cancellationToken)
    {
        var connectionOptions = options.Value;
        var sharedSecret = connectionOptions.SharedSecret;
        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unity RemoteDebug client connections are not configured.");
        }

        var registration = new RemoteDebugClientRegistration(
            GetQueryValue("name"),
            GetQueryValue("project"),
            GetQueryValue("unityVersion"),
            GetQueryValue("platform"),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!TryAuthorizeClient(sharedSecret, registration))
        {
            return Unauthorized();
        }

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return BadRequest("Unity RemoteDebug clients must connect with WebSocket.");
        }

        if (!clientRegistry.TryConnect(
                registration,
                connectionOptions.EffectiveMaxConcurrentClients,
                connectionOptions.EffectiveMaxConcurrentClientsPerRemoteEndPoint,
                out var session))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, "Unity RemoteDebug client connection limit reached.");
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();

        logger.LogInformation("Unity RemoteDebug client connected. ClientId={ClientId}, DisplayName={DisplayName}.", session.ClientId, session.DisplayName);
        try
        {
            await ReceiveUntilClosedAsync(
                socket,
                session.ClientId,
                connectionOptions.IdleTimeout,
                connectionOptions.LastSeenNotificationInterval,
                cancellationToken);
        }
        finally
        {
            clientRegistry.Disconnect(session.ClientId);
            logger.LogInformation("Unity RemoteDebug client disconnected. ClientId={ClientId}.", session.ClientId);
        }

        return new EmptyResult();
    }

    private async Task ReceiveUntilClosedAsync(
        WebSocket socket,
        Guid clientId,
        TimeSpan idleTimeout,
        TimeSpan notificationInterval,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveTimeout.CancelAfter(idleTimeout);

                ValueWebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(0, ReceiveBufferSize), receiveTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Idle timeout", CancellationToken.None);
                    return;
                }

                clientRegistry.Touch(clientId, notificationInterval);
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

    private bool TryAuthorizeClient(string sharedSecret, RemoteDebugClientRegistration registration)
    {
        if (!Request.Headers.TryGetValue(ChallengeIdHeader, out var challengeId) ||
            !Request.Headers.TryGetValue(ProofHeader, out var proofHeader))
        {
            return false;
        }

        if (!challengeStore.TryConsume(challengeId.ToString(), out var challenge) ||
            !TryBase64UrlDecode(proofHeader.ToString(), out var providedProof) ||
            providedProof.Length != ProofLength)
        {
            return false;
        }

        var expectedProof = ComputeProof(challenge, registration, sharedSecret);
        return CryptographicOperations.FixedTimeEquals(providedProof, expectedProof);
    }

    private string GetQueryValue(string key)
    {
        return Request.Query[key].ToString();
    }

    private static byte[] ComputeProof(
        RemoteDebugClientChallenge challenge,
        RemoteDebugClientRegistration registration,
        string sharedSecret)
    {
        var key = Encoding.UTF8.GetBytes(sharedSecret);
        using var hmac = new HMACSHA256(key);
        using var payload = new MemoryStream();

        WriteString(payload, challenge.ChallengeId);
        WriteBytes(payload, challenge.Nonce);
        WriteString(payload, registration.DisplayName ?? string.Empty);
        WriteString(payload, registration.ProjectName ?? string.Empty);
        WriteString(payload, registration.UnityVersion ?? string.Empty);
        WriteString(payload, registration.Platform ?? string.Empty);

        return hmac.ComputeHash(payload.ToArray());
    }

    private static void WriteString(Stream stream, string value)
    {
        WriteBytes(stream, Encoding.UTF8.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        stream.Write(length);
        stream.Write(value);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
