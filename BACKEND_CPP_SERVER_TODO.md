# C++ Backend Server TODO

This document tracks future work for C++ Backend/Dedicated server support.
The target design keeps Gateway data-plane traffic directly connected to the C++ server,
while a sidecar handles Master control-plane integration.

## Target Architecture

```text
                 control-plane
Master <----------------------------> Backend Sidecar
                                      |
                                      | local control IPC / shared state
                                      v
Client <----> Gateway <---- TCP ----> C++ Backend/Dedicated
                    data-plane
```

The sidecar must not relay gameplay packets. Gateway channel data must flow directly between
Gateway and the C++ Backend/Dedicated server.

## Design Principles

- Keep Gateway as the client-facing security boundary.
- Keep C++ Backend/Dedicated as the owner of Gateway-facing data-plane sockets.
- Use the sidecar only for Master control-plane work and local coordination.
- Avoid placing IPC or C# relay hops on the gameplay packet hot path.
- Keep Gateway-to-Backend transport compatible with the existing plain TCP internal-service model.
- Require direct-connect code validation before trusting a Gateway direct session.
- Treat C++ Backend/Dedicated CPU as harder to scale than Gateway CPU for channel/world-owned workloads.
- Keep future Backend framework features compatible across C# and C++ implementations.

## Phase 1: Protocol Spec

- [x] Document the `PacketCore` frame format for non-.NET implementations.
- [x] Document integer endianness, string encoding, GUID encoding, packet kind values, and payload length limits.
- [x] Document Gateway-to-Backend direct connection handshake requirements.
- [x] Document Gateway Backend channel frames:
  - [x] channel open
  - [x] channel data
  - [x] channel close
- [x] Document request/response exchange id rules.
- [x] Document which protocol values are compatibility metadata, not authorization secrets.

Implemented protocol specification: see `BACKEND_CPP_PROTOCOL.md` for PacketCore frame layout, primitive encodings, Gateway-to-Backend direct handshake, Gateway Backend channel frames, sidecar local control packets, and full-frame wire test vectors.

## Phase 2: Sidecar Responsibilities

- [x] Create a sidecar mode that connects to Master without opening the Gateway listener itself.
- [x] Configure the sidecar with the C++ server's Gateway-facing endpoint.
- [x] Advertise `BackendKind`, endpoint, and transport flags to Master on behalf of the C++ server.
- [x] Keep Master control-plane reconnect, status, and shutdown behavior equivalent to the C# Backend path.
- [x] Provide local direct-connect code validation for the C++ server.
- [x] Report C++ server health and Gateway session status through Master admin/status flows.
- [x] Share approved packet manifests or policy snapshots with the C++ server when needed.

Implemented sidecar entry point: set `GatewayListener:Enabled` to `false`. The sidecar keeps using `MasterConnection` for the control plane and advertises the configured `GatewayListener` endpoint as the external C++ data-plane listener without binding that port itself.

Implemented local validation callout: set `SidecarControl:Enabled` to `true`. The sidecar opens a loopback-only PacketCore control listener. C++ sends `DirectConnectCodeValidationRequest` as control packet id `1` and receives `DirectConnectCodeValidationResponse` as control packet id `2`.

## Phase 3: C++ Data Plane

- [x] Implement a C++ `PacketCore` reader/writer compatible with the C# implementation.
- [x] Implement Gateway direct connection accept/listen logic in C++.
- [x] Implement Gateway handshake handling in C++.
- [x] Call the sidecar for direct-connect code validation during handshake.
- [x] Accept trusted Gateway sessions only after validation succeeds.
- [x] Implement channel open/data/close envelope parsing.
- [x] Implement server-origin notify/request/response writes directly to Gateway.
- [x] Serialize writes per trusted Gateway connection.
- [x] Track channel-local state and remove it on channel close or Gateway disconnect.

Implemented C++ codec foundation: `CppBackendServer` provides a CMake-built C++20 PacketCore reader/writer plus Gateway Backend channel open/data/close and sidecar request codecs. `cpp_backend_packet_core_tests` verifies the same full-frame wire vectors as the C# tests.

Implemented C++ direct handshake state machine: C++ decodes Gateway `NodeHello` and `DirectConnectCode` frames, calls a direct-connect validation interface, rejects non-Gateway or mismatched validation identities, and emits `NodeAccepted` only after validation succeeds.

Implemented C++ sidecar validation client: `sidecar_direct_connect_code_validator` connects to the sidecar loopback PacketCore control endpoint, sends `DirectConnectCodeValidationRequest`, reads `DirectConnectCodeValidationResponse`, and rejects mismatched response ids. The Gateway listener accept loop is still pending.

Implemented C++ trusted Gateway session state: C++ tracks channel open/data/close events, rejects data for unknown channels, clears channel state on disconnect, and serializes Backend-origin channel data/close writes through a per-session write lock.

Implemented C++ Gateway direct listener: `gateway_direct_listener` binds a TCP endpoint, accepts a Gateway connection, sends the Master-compatible auth challenge, reads `NodeHello` and `DirectConnectCode`, validates through the configured validator, and writes `NodeAccepted` after successful validation.

## Phase 4: Sidecar And C++ Local Contract

- [x] Define the local IPC/shared-state protocol between sidecar and C++ server.
- [x] Keep local IPC off the per-packet gameplay data path.
- [x] Support direct-connect code validation requests and responses.
- [x] Support health/status updates from C++ server to sidecar.
- [x] Support endpoint readiness signaling so sidecar does not advertise an unreachable C++ listener.
- [x] Support graceful shutdown coordination.
- [x] Decide whether local communication uses named pipes, Unix domain sockets, TCP loopback, shared memory, or platform-specific primitives.

Implemented endpoint readiness signaling: set `SidecarControl:RequireEndpointReadyBeforeAdvertise` to `true`. C++ sends `SidecarEndpointStateUpdate` as control packet id `3`; the sidecar acknowledges with `SidecarEndpointStateAck` as control packet id `4` and delays Master endpoint advertisement until readiness is true.

Implemented runtime status reporting: C++ sends `SidecarRuntimeStatusUpdate` as control packet id `5`; the sidecar acknowledges with `SidecarRuntimeStatusAck` as control packet id `6` and includes the latest health/session/channel counters in Master admin/status output.

Implemented graceful shutdown coordination: C++ sends `SidecarShutdownStateUpdate` as control packet id `7`; the sidecar acknowledges with `SidecarShutdownStateAck` as control packet id `8`, marks the endpoint not ready, and reports the shutdown reason in Master admin/status output.

## Phase 5: C# And C++ Backend Framework Compatibility

- [ ] Treat Backend framework features as cross-language features after C++ Backend support lands.
- [ ] When adding a Backend framework feature in C#, either implement the C++ equivalent or document the compatibility boundary.
- [ ] When adding a Backend framework feature in C++, either implement the C# equivalent or document the compatibility boundary.
- [ ] Keep wire protocol changes backward-compatible during rolling deployments.
- [x] Add shared protocol/version documentation before changing Gateway, Master, or Backend channel contracts.
- [x] Add cross-language test vectors for every shared wire codec.
- [ ] Prefer schema or manifest sources that can generate both C# and C++ code when practical.
- [ ] Avoid C#-only assumptions in Master/Gateway behavior that would block C++ Backend nodes.
- [ ] Avoid C++-only assumptions that would make C# Backend nodes second-class implementations.

Implemented cross-language wire vectors: C# and C++ tests now pin PacketCore framing, Master direct-handshake payloads, Gateway Backend channel envelopes, sidecar validation/status/shutdown/manifest local-control packets, and manifest snapshot responses.

## Phase 6: Packet Manifest Integration

- [x] Ensure the C++ server can declare the active packet manifest id/hash.
- [x] Have the sidecar advertise the C++ server's manifest id/hash to Master.
- [x] Ensure Master approval and Gateway snapshot behavior works for C++ Backend nodes.
- [x] Provide C++ access to manifest metadata needed for fast parsers and debug validation.
- [ ] Keep Gateway verifier policy compatible with both C# and C++ Backend runtimes.

Implemented manifest declaration: set `SidecarControl:RequireManifestBeforeAdvertise` to `true`. C++ sends `SidecarManifestDeclarationUpdate` as control packet id `9`; the sidecar acknowledges with `SidecarManifestDeclarationAck` as control packet id `10` and advertises the declared manifest id/hash to Master.

Implemented manifest snapshot sharing: Master sends approved `BackendPacketManifestSnapshot` updates to Backend sidecars as well as Gateways. C++ sends `SidecarManifestSnapshotRequest` as control packet id `11`; the sidecar responds with `SidecarManifestSnapshotResponse` as control packet id `12`, carrying the latest approved manifest snapshot using the shared Master control-plane manifest codec.

## Phase 7: Validation And Tests

- [x] Add C# and C++ wire codec compatibility test vectors.
- [x] Add integration tests for sidecar Master registration with a C++ endpoint advertisement.
- [x] Add integration tests for Gateway direct connection to a C++ Backend test server.
- [x] Add handshake rejection tests for invalid direct-connect codes.
- [x] Add channel open/data/close tests against the C++ data-plane implementation.
- [ ] Add shutdown and reconnect tests for sidecar and C++ server coordination.
- [ ] Add performance tests that confirm sidecar IPC is not on the gameplay packet hot path.

## Open Questions

- Should the first implementation use a C# sidecar, a C++ Master client, or support both?
- Which local IPC primitive gives the best balance of reliability, portability, and operational simplicity?
- Should the sidecar own all Master-facing admin status, or should the C++ server provide detailed status snapshots?
- How should the system behave if the sidecar is connected to Master but the C++ listener becomes unhealthy?
- Should Gateway route-open responses expose any C++ Backend capability or manifest metadata to clients?
