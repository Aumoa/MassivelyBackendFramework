# Backend Server API

This document describes the shared API for building Backend servers that connect to Gateway and Master.

The primary goal is to allow new Backend kinds without rebuilding Gateway or Master. New Backends should use `BackendServer.Abstractions` and `BackendServer.Core`, while Backend kinds, credentials, and route policy are managed through MasterAdmin and persisted configuration data.

## Project References

A Backend server project should reference these projects.

```xml
<ItemGroup>
  <ProjectReference Include="..\BackendServer.Abstractions\BackendServer.Abstractions.csproj" />
  <ProjectReference Include="..\BackendServer.Core\BackendServer.Core.csproj" />
</ItemGroup>
```

`BackendServer.Abstractions` provides the runtime contracts that Backend implementations consume and implement.

`BackendServer.Core` provides the Master control-plane connection, Gateway direct connection listener, direct-connect code validation, Gateway channel lifecycle dispatch, and server-side push sender.

## Service Registration

A Backend server registers its runtime as `IBackendRuntime`, then calls `AddBackendServer`.

```csharp
using BackendServer.Extensions;
using BackendServer.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IBackendRuntime, MyBackendRuntime>();
builder.Services.AddBackendServer(builder.Configuration);

await builder.Build().RunAsync();
```

`AddBackendServer` uses `TryAddSingleton<IBackendRuntime, NoOpBackendRuntime>()`. If the real runtime is registered first, the default no-op runtime is not registered.

DedicatedServer uses the same structure. `AddDedicatedServer` is currently only a thin wrapper around `AddBackendServer`; there is no separate Dedicated-specific connection API.

## Configuration Template

A Backend server needs `MasterConnection` and `GatewayListener` configuration.

```json
{
  "GatewayListener": {
    "Enabled": true,
    "IPAddress": "::1",
    "Port": 11701,
    "UseTls": false,
    "CertificateSubjectName": "localhost",
    "Backlog": 512,
    "HandshakeTimeoutMilliseconds": 5000
  },
  "SidecarControl": {
    "Enabled": false,
    "RequireEndpointReadyBeforeAdvertise": false,
    "IPAddress": "127.0.0.1",
    "Port": 11702,
    "Backlog": 64,
    "RequestTimeoutMilliseconds": 5000
  },
  "MasterConnection": {
    "Enabled": true,
    "IPAddress": "::1",
    "Port": 11601,
    "UseTls": false,
    "ServerName": "localhost",
    "NodeId": "my-backend-local",
    "DisplayName": "My Backend",
    "BackendKind": "my-backend-kind",
    "BackendPacketManifestId": "my-backend:v1",
    "BackendPacketManifestHash": "<sha256-hex-from-approved-manifest>",
    "SharedSecret": "<issued-by-master-admin>",
    "ReconnectDelayMilliseconds": 5000,
    "HandshakeTimeoutMilliseconds": 5000
  }
}
```

`MasterConnection:NodeId` is the node identifier registered with Master.

`MasterConnection:BackendKind` is the Backend kind used by Gateway route policy and client route-open requests. Adding a new kind should not require rebuilding Gateway or Master; credentials and route policy are managed through MasterAdmin.

`MasterConnection:BackendPacketManifestId` and `MasterConnection:BackendPacketManifestHash` identify the active packet manifest that this Backend binary or configuration expects. Master accepts the Backend advertisement only when that id/hash is approved for the authorized `BackendKind`, then publishes the approved manifest snapshot to Gateways.

`MasterConnection:SharedSecret` must use the Backend service connection credential issued by MasterAdmin. Do not commit this value to the repository.

`GatewayListener` is the Backend-side listener that Gateway connects to after receiving a direct-connect code. In production, configure this together with private networking and TLS.

Set `GatewayListener:Enabled` to `false` for C++ Backend sidecar mode. In that mode the C# sidecar does not open the Gateway listener or load a local TLS certificate. The configured `GatewayListener` address, port, and TLS flag are still advertised to Master as the external data-plane endpoint owned by the C++ Backend/Dedicated process.

Set `SidecarControl:Enabled` to `true` when a C++ Backend/Dedicated process needs the sidecar to validate Gateway direct-connect codes. The sidecar opens a loopback-only PacketCore control listener and accepts `DirectConnectCodeValidationRequest` frames on packet id `1`, then returns `DirectConnectCodeValidationResponse` frames on packet id `2`. This local protocol is for handshake/control work only; gameplay channel packets must stay on the direct Gateway-to-C++ data-plane socket.

Set `SidecarControl:RequireEndpointReadyBeforeAdvertise` to `true` when the C++ process should explicitly signal that its Gateway-facing listener is reachable before the sidecar advertises the endpoint to Master. The C++ process sends `SidecarEndpointStateUpdate` as control packet id `3`, and the sidecar returns `SidecarEndpointStateAck` as control packet id `4`.

The C++ process can report runtime health through `SidecarRuntimeStatusUpdate` as control packet id `5`. The sidecar acknowledges with `SidecarRuntimeStatusAck` as packet id `6` and includes the latest health, active Gateway session count, active channel count, and detail text in Master admin/status responses.

## Runtime Lifecycle

Backend runtime code implements `IBackendRuntime`.

```csharp
using BackendServer.Runtime;

public sealed class MyBackendRuntime(
    IBackendGatewayChannelSender sender,
    ILogger<MyBackendRuntime> logger) : IBackendRuntime
{
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleGatewayChannelOpenedAsync(
        BackendGatewayChannelOpenContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Channel opened. Gateway={GatewayNodeId}, Channel={ChannelId}, Principal={PrincipalSubjectId}",
            context.GatewayNodeId,
            context.ChannelId,
            context.PrincipalSubjectId);

        await sender.SendNotifyAsync(
            context.Channel,
            packetId: 1001,
            version: 1,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken);
    }

    public ValueTask HandleGatewayPacketAsync(
        BackendGatewayPacketContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Packet received. Kind={Kind}, PacketId={PacketId}, Channel={ChannelId}",
            context.Kind,
            context.PacketId,
            context.ChannelId);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleGatewayChannelClosedAsync(
        BackendGatewayChannelCloseContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Channel closed. Channel={ChannelId}, Reason={Reason}",
            context.ChannelId,
            context.Reason);

        return ValueTask.CompletedTask;
    }
}
```

### `HandleGatewayChannelOpenedAsync`

Called when Gateway opens a persistent route and creates a Backend channel.

From this point on, the runtime can use `context.Channel` for server-side push. The Backend can send a notify or request before the client sends its first routed packet.

`PrincipalSubjectId` is populated when Gateway client authentication is enabled and the client has an authenticated principal. It can be `null` in unauthenticated flows.

### `HandleGatewayPacketAsync`

Called when Gateway forwards a client-origin packet or client-origin response to the Backend.

`BackendGatewayPacketContext` includes `Channel`, `Kind`, `PacketId`, `Version`, and `ExchangeId`.

`Request` and `Response` packets must have an `ExchangeId`. `Notify` packets do not have an exchange id.

### `HandleGatewayChannelClosedAsync`

Called when a Gateway route closes or a client disconnect closes the Backend channel.

Runtime code should clean up channel-local state here. Gateway may reject server-side push attempts for closed channels, and the runtime should remove any locally retained channel state to avoid unnecessary sends.

## Server-Side Push

Backend runtime code can receive `IBackendGatewayChannelSender` through DI and send packets to Gateway.

```csharp
await sender.SendNotifyAsync(
    channel,
    packetId: 2001,
    version: 1,
    payload,
    cancellationToken);

await sender.SendRequestAsync(
    channel,
    packetId: 2002,
    version: 1,
    exchangeId: Guid.NewGuid(),
    payload,
    cancellationToken);

await sender.SendResponseAsync(
    channel,
    packetId: 2003,
    version: 1,
    exchangeId: requestExchangeId,
    payload,
    cancellationToken);

await sender.CloseAsync(
    channel,
    "backend closed channel",
    cancellationToken);
```

`SendNotifyAsync` sends a Backend-origin notify to the client.

`SendRequestAsync` sends a Backend-origin request to the client. The client response must return with the same exchange id.

`SendResponseAsync` sends a response to a client-origin request. Gateway rejects responses that do not match a pending client-origin exchange.

`CloseAsync` is used when the Backend wants to close the channel. Gateway forwards a route-close notify to the client.

## Trust And Validation Model

Master issues and validates direct-connect codes between Gateway and Backend. Backend asks Master to validate the direct-connect code presented by Gateway, then trusts the Gateway connection only after confirming that the validation target node kind is `Backend`.

Gateway is the client-facing trust boundary. Gateway performs client authentication, route token validation, route ownership checks, exchange matching, and Backend binding validation.

Gateway also validates routed packets against the approved Backend packet manifest selected by the trusted Backend session. The manifest is keyed by `BackendKind`, manifest id, and manifest hash, with entries for direction, packet kind, packet id, routed version, payload length constraints, and optional schema metadata.

Gateway rejects client-to-Backend and Backend-to-client routed packets when the manifest does not contain the packet contract or when the payload length is outside the approved range. Deprecated packet versions remain accepted while they are still present in an approved or deprecated manifest, which supports rolling deployments. Removing a manifest from Master storage revokes it for new Backend advertisements and future Gateway snapshots.

Backend can trust packets that arrive from an authenticated Gateway connection to have passed route ownership, exchange matching, and manifest compatibility checks, but it should still keep cheap protocol sanity checks such as packet kind, packet id, version, and payload length. Gameplay rules and authoritative state validation remain Backend runtime responsibilities, and manifest validation never replaces state-based authorization or game-rule decisions.

## Adding A New Backend

1. Create a new server project and reference `BackendServer.Abstractions` and `BackendServer.Core`.
2. Add an `IBackendRuntime` implementation.
3. Inject `IBackendGatewayChannelSender` into the runtime if server-side push is needed.
4. Register `services.AddSingleton<IBackendRuntime, MyBackendRuntime>()`.
5. Call `services.AddBackendServer(configuration)`.
6. Issue a Backend service credential in MasterAdmin and configure it as `MasterConnection:SharedSecret`.
7. Set `MasterConnection:BackendKind` to the desired Backend kind.
8. Approve a Backend packet manifest in MasterAdmin, then configure `MasterConnection:BackendPacketManifestId` and `MasterConnection:BackendPacketManifestHash` to match that approved manifest.
9. Allow that Backend kind in Gateway route policy through MasterAdmin.
10. Configure private network reachability, ports, and TLS between Gateway and Backend.
11. Clients use the same Backend kind when they request route open through Gateway.

## Current Boundaries

The Backend API provides Gateway direct connection handling and route channel lifecycle dispatch. The Backend runtime owns channel owner lanes, world state, gameplay validation, and packet payload serialization.

The sender serializes writes to each active Gateway connection. If channel-level gameplay ordering or mailbox dispatch is needed, the runtime should build that execution model around `BackendGatewayChannel`.

Gateway remains the final authority for route token ownership, Backend binding, exchange id matching, and manifest compatibility. Runtime code should still clear closed channel state to avoid unnecessary push attempts.

C++ sidecar mode currently covers Master control-plane registration and endpoint advertisement only. The C++ data-plane process must own the Gateway-facing listener and implement the Gateway handshake, direct-connect code validation callout, channel envelopes, and packet writes.

The sidecar now provides a local direct-connect validation callout, but it still does not relay Gateway channel traffic. The C++ data-plane process remains responsible for accepting Gateway sessions only after a successful local validation response.

When endpoint readiness is required, Master advertisement is delayed until the sidecar receives a ready endpoint state update from the C++ data-plane process.

The sidecar status surface includes the latest C++ runtime health and Gateway session counters reported over the local control listener.
