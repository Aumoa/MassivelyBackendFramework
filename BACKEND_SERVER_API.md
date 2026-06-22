# Backend Server API

이 문서는 Gateway와 Master에 연결되는 Backend 서버를 새로 만들 때 참고하는 공통 API 안내서입니다.

핵심 목표는 Backend 종류가 늘어나도 Gateway나 Master 코드를 다시 빌드하지 않는 것입니다. 새 Backend는 `BackendServer.Abstractions`와 `BackendServer.Core`를 사용하고, Backend 종류와 라우팅 허용 정책은 MasterAdmin/설정 데이터로 관리합니다.

## 프로젝트 참조

Backend 서버 프로젝트는 다음 프로젝트를 참조합니다.

```xml
<ItemGroup>
  <ProjectReference Include="..\BackendServer.Abstractions\BackendServer.Abstractions.csproj" />
  <ProjectReference Include="..\BackendServer.Core\BackendServer.Core.csproj" />
</ItemGroup>
```

`BackendServer.Abstractions`는 Backend 런타임이 구현할 계약을 제공합니다.

`BackendServer.Core`는 Master control-plane 연결, Gateway direct connection listener, direct-connect code 검증, Gateway channel lifecycle dispatch, server-side push sender를 제공합니다.

## 서비스 등록

Backend 서버는 자체 runtime을 `IBackendRuntime`으로 등록하고 `AddBackendServer`를 호출합니다.

```csharp
using BackendServer.Extensions;
using BackendServer.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IBackendRuntime, MyBackendRuntime>();
builder.Services.AddBackendServer(builder.Configuration);

await builder.Build().RunAsync();
```

`AddBackendServer`는 `TryAddSingleton<IBackendRuntime, NoOpBackendRuntime>()`를 사용합니다. 실제 runtime을 먼저 등록하면 기본 no-op runtime은 등록되지 않습니다.

DedicatedServer도 같은 구조를 사용합니다. `AddDedicatedServer`는 현재 `AddBackendServer`를 호출하는 얇은 wrapper일 뿐이며, 별도 Dedicated 전용 연결 API는 없습니다.

## 설정 템플릿

Backend 서버는 `MasterConnection`과 `GatewayListener` 설정이 필요합니다.

```json
{
  "GatewayListener": {
    "IPAddress": "::1",
    "Port": 11701,
    "UseTls": false,
    "CertificateSubjectName": "localhost",
    "Backlog": 512,
    "HandshakeTimeoutMilliseconds": 5000
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
    "SharedSecret": "<issued-by-master-admin>",
    "ReconnectDelayMilliseconds": 5000,
    "HandshakeTimeoutMilliseconds": 5000
  }
}
```

`MasterConnection:NodeId`는 Master에 등록되는 노드 식별자입니다.

`MasterConnection:BackendKind`는 Gateway route policy와 client route open 요청에서 사용하는 Backend 종류입니다. 새 종류를 추가할 때 Gateway/Master rebuild가 필요하지 않아야 하며, MasterAdmin에서 credential과 route policy를 관리합니다.

`MasterConnection:SharedSecret`은 MasterAdmin에서 발급한 Backend 서비스 연결 credential 값을 사용합니다. 이 값은 저장소에 커밋하지 않습니다.

`GatewayListener`는 Gateway가 direct-connect code를 받은 뒤 접속하는 Backend-side listener입니다. 운영 환경에서는 private network와 TLS 설정을 함께 고려합니다.

## Runtime lifecycle

Backend runtime은 `IBackendRuntime`을 구현합니다.

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

Gateway가 persistent route를 열고 Backend channel을 생성했을 때 호출됩니다.

이 시점부터 `context.Channel`을 사용해 server-side push를 보낼 수 있습니다. Client가 첫 packet을 보내기 전에도 Backend가 먼저 notify/request를 보낼 수 있습니다.

`PrincipalSubjectId`는 Gateway client 인증이 설정되어 있고 인증된 principal이 있을 때 전달됩니다. 인증이 없는 흐름에서는 `null`일 수 있습니다.

### `HandleGatewayPacketAsync`

Gateway가 client-origin packet 또는 client-origin response를 Backend로 전달할 때 호출됩니다.

`BackendGatewayPacketContext`에는 `Channel`, `Kind`, `PacketId`, `Version`, `ExchangeId`가 포함됩니다.

`Request`와 `Response` packet은 `ExchangeId`를 가져야 합니다. `Notify` packet은 exchange id가 없습니다.

### `HandleGatewayChannelClosedAsync`

Gateway route가 닫히거나 client가 disconnect되어 Backend channel이 닫혔을 때 호출됩니다.

Runtime은 이 이벤트에서 channel-local 상태를 정리해야 합니다. 닫힌 channel에 대한 server-side push는 Gateway에서 거부될 수 있으며, runtime 쪽에서도 보관한 channel state를 정리하는 것이 좋습니다.

## Server-side push

Backend runtime은 `IBackendGatewayChannelSender`를 DI로 받아 Gateway로 packet을 보낼 수 있습니다.

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

`SendNotifyAsync`는 Backend-origin notify를 client로 전달합니다.

`SendRequestAsync`는 Backend-origin request를 client로 전달합니다. Client response는 같은 exchange id로 돌아와야 합니다.

`SendResponseAsync`는 client-origin request에 대한 response를 client로 전달합니다. Gateway는 pending client-origin exchange와 일치하지 않는 response를 거부합니다.

`CloseAsync`는 Backend가 channel을 닫고 싶을 때 사용합니다. Gateway는 client에게 route close notify를 전달합니다.

## Trust and validation model

Master는 Gateway와 Backend 사이의 direct-connect code를 발급하고 검증합니다. Backend는 Gateway가 제시한 direct-connect code를 Master에 검증 요청하고, 검증 결과의 target node kind가 `Backend`인지 확인한 뒤 trusted Gateway connection으로 전환합니다.

Gateway는 client-facing trust boundary입니다. Client 인증, route token 검증, route ownership 검증, exchange matching, Backend binding 검증은 Gateway가 수행합니다.

Backend는 authenticated Gateway connection에서 온 packet을 신뢰할 수 있지만, packet kind, packet id, version, payload shape 같은 저비용 protocol sanity check와 게임 규칙 검증은 유지해야 합니다.

## 새 Backend 추가 절차

1. 새 서버 프로젝트를 만들고 `BackendServer.Abstractions`, `BackendServer.Core`를 참조합니다.
2. `IBackendRuntime` 구현체를 추가합니다.
3. `IBackendGatewayChannelSender`가 필요하면 runtime 생성자에서 DI로 받습니다.
4. `services.AddSingleton<IBackendRuntime, MyBackendRuntime>()`를 등록합니다.
5. `services.AddBackendServer(configuration)`를 호출합니다.
6. MasterAdmin에서 Backend 서비스 credential을 발급하고 `MasterConnection:SharedSecret`에 설정합니다.
7. `MasterConnection:BackendKind`를 원하는 Backend 종류로 설정합니다.
8. MasterAdmin에서 Gateway route policy에 해당 Backend 종류를 허용합니다.
9. Gateway와 Backend가 서로 접근 가능한 private network, port, TLS 설정을 맞춥니다.
10. Client는 Gateway에 route open을 요청할 때 같은 Backend kind를 사용합니다.

## 현재 알려진 경계

Backend API는 Gateway direct connection과 route channel lifecycle을 제공합니다. Backend 내부의 channel owner lane, world state, gameplay validation, packet payload serialization은 각 Backend runtime의 책임입니다.

Sender는 active Gateway connection에 대한 write를 serialize합니다. Channel별 gameplay ordering이나 mailbox dispatch가 필요하면 runtime에서 `BackendGatewayChannel` 기준으로 별도 실행 모델을 구성합니다.

Gateway가 최종적으로 route token ownership, Backend binding, exchange id matching을 검증합니다. Runtime은 닫힌 channel state를 정리해 불필요한 push 시도를 줄여야 합니다.
