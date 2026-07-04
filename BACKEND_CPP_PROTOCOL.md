# C++ Backend Wire Protocol

This document is the wire contract for non-.NET Backend/Dedicated implementations.
It describes the PacketCore frame format, Gateway-to-Backend direct connection
handshake, Gateway Backend channel frames, and sidecar control metadata that a C++
runtime must implement.

## PacketCore Frame

Every frame starts with an 8-byte header followed by `PayloadLength` payload bytes.
Header fields use network byte order except for the packed first byte.

| Offset | Size | Field | Encoding |
| --- | ---: | --- | --- |
| 0 | 1 | kind and flags | high 2 bits are `PacketKind`; low 6 bits are `PacketFlags` |
| 1 | 2 | packet id | unsigned 16-bit big-endian, non-zero |
| 3 | 2 | version | unsigned 16-bit big-endian, non-zero |
| 5 | 3 | payload length | unsigned 24-bit big-endian, max `0xFFFFFF` |

`PacketKind` values are:

| Value | Name |
| ---: | --- |
| 0 | Request |
| 1 | Response |
| 2 | Notify |
| 3 | Control |

Only `PacketFlags.None` (`0`) is currently defined. Readers on untrusted streams
must reject unknown flags.

## Primitive Encoding

- `byte`: one byte.
- `ushort`: 2-byte unsigned integer, big-endian.
- `uint`: 4-byte unsigned integer, big-endian.
- `int`: 4-byte signed integer, big-endian.
- `long`: 8-byte signed integer, big-endian.
- `string`: 4-byte signed big-endian UTF-8 byte length followed by UTF-8 bytes.
  Negative lengths are invalid.
- `Guid`: 16 bytes in .NET `Guid.TryWriteBytes` order. For example,
  `00112233-4455-6677-8899-aabbccddeeff` is encoded as
  `33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff`.

## Gateway-To-Backend Direct Handshake

The C++ Backend listener accepts a Gateway TCP connection on the endpoint advertised
by the sidecar. TLS is controlled by the advertised endpoint's `UseTls` flag.

Handshake frames use `MasterControlProtocol.SchemaVersion` and `PacketKind.Control`:

1. Backend sends `NodeAuthChallenge` with packet id `100`.
2. Gateway sends `NodeHello` with packet id `101`.
   `NodeKind` must be `Gateway`, `ProtocolVersion` must match the current Master
   control schema version, and `MasterConnectionId` must be present.
3. Gateway sends `DirectConnectCode` with packet id `115`.
4. Backend validates the code through the sidecar local control channel before
   trusting the Gateway session. Validation must confirm the Gateway node id,
   Gateway Master connection id, and `TargetNodeKind=Backend`.
5. Backend sends `NodeAccepted` with packet id `103`.

The direct-connect code is an authorization secret. It is single-purpose validation
material issued by Master and checked through the sidecar. Packet ids, schema
versions, manifest ids, manifest hashes, BackendKind, channel ids, route tokens,
and exchange ids are compatibility or routing metadata; they are not authorization
secrets and must not be trusted without the server-side checks above.

## Gateway Backend Channel Frames

After handshake, Gateway and Backend exchange PacketCore frames directly. The
sidecar is not on this data path.

| Packet id | Version | Kind | Direction | Payload |
| ---: | ---: | --- | --- | --- |
| `10` | `1` | Notify | Gateway to Backend | `GatewayBackendChannelOpen` |
| `8` | `1` | Request, Response, or Notify | both | `GatewayBackendChannelDataEnvelope` |
| `9` | `1` | Notify | both | `GatewayBackendChannelClose` |

### Channel Open

`GatewayBackendChannelOpen` payload:

| Field | Encoding | Notes |
| --- | --- | --- |
| ChannelId | `uint` | Non-zero Gateway-assigned channel id |
| HasPrincipalSubjectId | `byte` | `0` when absent, non-zero when present |
| PrincipalSubjectId | `string` | Present only when `HasPrincipalSubjectId` is non-zero |

### Channel Data

`GatewayBackendChannelDataEnvelope` payload:

| Field | Encoding | Notes |
| --- | --- | --- |
| ChannelId | `uint` | Existing open channel |
| RoutedKind | `byte` | Request, Response, or Notify |
| RoutedPacketId | `ushort` | Non-zero application packet id |
| RoutedVersion | `ushort` | Non-zero application packet version |
| HasExchangeId | `byte` | `0` when absent, non-zero when present |
| ExchangeId | `Guid` | Required for routed Request and Response |
| RoutedPayloadLength | `int` | Non-negative byte count |
| RoutedPayload | bytes | Application payload |

The outer frame kind must match `RoutedKind`. Request and Response frames require
an exchange id; Notify frames may omit it.

### Channel Close

`GatewayBackendChannelClose` payload:

| Field | Encoding | Notes |
| --- | --- | --- |
| ChannelId | `uint` | Channel being closed |
| Reason | `string` | Human-readable close reason |

## Request/Response Exchange Ids

`GatewayBackendExchangeId` is a GUID used to pair routed Requests and Responses
inside a channel data envelope.

- Request originators create a new non-empty exchange id.
- The matching Response must reuse the same exchange id.
- Gateway tracks client-origin and Backend-origin pending exchanges and rejects
  unexpected responses.
- Exchange ids are correlation metadata, not authorization material.

## Sidecar Local Control Packets

The C++ runtime talks to the C# sidecar over loopback PacketCore control frames
using `BackendSidecarControlProtocol.SchemaVersion = 1`.

| Packet id | Payload |
| ---: | --- |
| `1` | `DirectConnectCodeValidationRequest` |
| `2` | `DirectConnectCodeValidationResponse` |
| `3` | `SidecarEndpointStateUpdate` |
| `4` | `SidecarEndpointStateAck` |
| `5` | `SidecarRuntimeStatusUpdate` |
| `6` | `SidecarRuntimeStatusAck` |
| `7` | `SidecarShutdownStateUpdate` |
| `8` | `SidecarShutdownStateAck` |
| `9` | `SidecarManifestDeclarationUpdate` |
| `10` | `SidecarManifestDeclarationAck` |
| `11` | `SidecarManifestSnapshotRequest` |
| `12` | `SidecarManifestSnapshotResponse` |

Manifest snapshot responses carry the same `BackendPacketManifestSnapshot` codec
used by Master and Gateway. The snapshot is approved compatibility metadata for
fast parsers and debug validation; Gateway still performs server-side packet
manifest verification.

## Wire Test Vectors

The C# tests in `GatewayServer.Tests/Protocols/BackendCppWireVectorTests.cs`
assert these full-frame hex vectors. A C++ implementation should produce and
consume the same bytes.

| Name | Hex |
| --- | --- |
| PacketCore control header, no payload | `c000640008000000` |
| Gateway Backend channel open | `80000a0001000011010203040100000008706c617965722d31` |
| Gateway Backend channel data request | `00000800010000220102030400123400020133221100554477668899aabbccddeeff00000004deadbeef` |
| Gateway Backend channel close | `800009000100000c0102030400000004646f6e65` |
| Sidecar direct-connect validation request | `c00001000100003333221100554477668899aabbccddeeff00000006636f64652d3100000009676174657761792d61000000086d61737465722d61` |
| Sidecar manifest snapshot request | `c0000b000100001033221100554477668899aabbccddeeff` |
