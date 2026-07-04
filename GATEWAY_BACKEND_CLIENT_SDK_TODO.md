# Gateway Backend Client SDK TODO

This document tracks future work for a generic client SDK that connects external clients
to Backend/Dedicated services through Gateway routes.

The target audience includes Unity clients, but the SDK design should keep the Gateway layer
generic enough for other clients and future Backend services.

## Target Architecture

```text
Unity Game Code
  -> Backend-specific SDK
    -> generic Gateway Backend route SDK
      -> Gateway protocol
        -> Gateway
          -> Backend/Dedicated
```

The Gateway SDK owns Gateway connection, authentication, server listing, route opening,
route tokens, exchange ids, raw routed packet send/receive, timeout handling, and route closure.

Backend-specific SDKs receive an already opened Gateway route and layer typed service APIs on top
of that route. They own packet ids, payload encoding/decoding, service-specific events, and
descriptor JSON interpretation.

## Design Principles

- Keep the client physically connected only to Gateway.
- Treat Backend/Dedicated connectivity as a logical route over Gateway.
- Keep the generic Gateway SDK unaware of game payload semantics.
- Keep Backend-specific SDKs unaware of Gateway authentication and server discovery internals where practical.
- Pass an opened route abstraction into Backend-specific SDKs instead of giving them full Gateway client ownership.
- Let multiple Backend-specific SDKs share one Gateway session through separate routes.
- Keep route-open server selection based on opaque Gateway server handles, not client-submitted descriptor fields.
- Do not expose Backend/Dedicated private endpoints through SDK models.
- Avoid adding game-specific concepts such as world, channel, map, or match fields to the generic SDK until a common contract is proven.
- Delay actual Backend-specific SDK implementations until real Backend services exist.

## Phase 1: Generic Gateway SDK Surface

- [ ] Design a `GatewayClient` abstraction for connect, disconnect, authentication, and session state.
- [ ] Design server directory APIs:
  - [ ] list servers by `BackendKind`
  - [ ] expose opaque `ServerHandle`
  - [ ] expose descriptor version/hash
  - [ ] expose descriptor JSON as opaque client data
- [ ] Design route-open APIs:
  - [ ] open by `BackendKind` for auto-selection
  - [ ] open by `ServerHandle` for explicit server selection
  - [ ] close route
- [ ] Design route lifecycle events:
  - [ ] opened
  - [ ] closed by client
  - [ ] closed by Gateway
  - [ ] closed by Backend
  - [ ] reconnect required
- [ ] Design error models for authentication failure, server-list failure, stale handle, route rejection, timeout, and protocol mismatch.

## Phase 2: Raw Route API

- [ ] Define an `IBackendRoute` or equivalent route abstraction.
- [ ] Include route identity fields such as `BackendKind`, route token fingerprint, selected server handle, and descriptor hash when available.
- [ ] Provide raw notify send API:
  - [ ] `PacketId`
  - [ ] version
  - [ ] payload bytes
- [ ] Provide raw request/response API:
  - [ ] exchange id ownership
  - [ ] timeout
  - [ ] cancellation
  - [ ] response packet id/version/payload
- [ ] Provide raw response send API for Backend-origin client requests.
- [ ] Provide inbound event streams or callbacks for notify, request, response, and route close.
- [ ] Ensure raw APIs preserve packet ordering guarantees expected by Gateway protocol.
- [ ] Make backpressure and send queue behavior explicit.

## Phase 3: Backend-Specific SDK Layering

- [ ] Define the recommended constructor pattern for service SDKs:
  - [ ] accept an already opened `IBackendRoute`
  - [ ] validate expected `BackendKind` or descriptor hash when applicable
  - [ ] subscribe to route inbound events
- [ ] Define typed service API guidance:
  - [ ] encode domain request payloads
  - [ ] decode domain response payloads
  - [ ] translate notify packets into typed events
  - [ ] keep Gateway route errors visible to callers
- [ ] Provide a convenience pattern that can list servers, select a server, open a route, and return a typed SDK client.
- [ ] Keep the convenience pattern optional so advanced clients can manage Gateway routes directly.
- [ ] Ensure Backend-specific SDKs do not bypass Gateway route ownership or authentication rules.

## Phase 4: Descriptor JSON And Server Selection

- [ ] Let the generic Gateway SDK expose descriptor JSON without interpreting game-specific fields.
- [ ] Let Backend-specific SDKs or game code parse descriptor JSON according to their own service contract.
- [ ] Use `ServerHandle` from the server list response when opening routes.
- [ ] Avoid route-open APIs that ask clients to resubmit descriptor JSON fields such as `serverId`.
- [ ] Expose descriptor version/hash so Backend-specific SDKs can reject incompatible descriptors.
- [ ] Document that Gateway remains the authority for whether a selected handle is routable.

## Phase 5: Client-Side Manifest Awareness

- [ ] Decide whether the generic SDK should expose packet manifest id/hash for the selected route.
- [ ] Let Backend-specific SDKs use manifest data for development-time assertions when useful.
- [ ] Keep client-side manifest checks as developer ergonomics only, not security boundaries.
- [ ] Add hooks for generated packet codecs if packet schemas later generate both client and server code.

## Phase 6: Reconnect And Route Recovery

- [ ] Define Gateway reconnect behavior separately from Backend route recovery.
- [ ] Decide when routes are always lost after Gateway reconnect.
- [ ] Design reconnect tickets or route restore tokens only after the server directory and route-open models settle.
- [ ] Keep reconnect behavior explicit in both generic SDK and Backend-specific SDK APIs.
- [ ] Surface enough close reasons for game code to decide whether to retry, return to server selection, or abort.

## Phase 7: Unity Packaging

- [ ] Decide whether the first SDK target is Unity-only or a general .NET client SDK with Unity packaging.
- [ ] Keep Unity main-thread dispatch concerns outside the core transport when possible.
- [ ] Provide Unity-friendly async/event adapters.
- [ ] Avoid allocations in high-frequency receive paths where practical.
- [ ] Add sample Unity usage only after the generic SDK shape is stable.

## Phase 8: Guidance For Future Backend Services

- [ ] After the generic SDK design is accepted, update repository guidance for future Backend service authors.
- [ ] Do not update `AGENTS.md` or create a skill until the SDK shape is concrete enough to avoid churn.
- [ ] Decide whether the guidance belongs in `AGENTS.md`, a dedicated repository skill, or both.
- [ ] If the guidance is reusable and workflow-like, prefer extracting it into a skill instead of expanding `AGENTS.md`.
- [ ] The future guidance should tell Backend service authors to:
  - [ ] expose typed client APIs over an opened `IBackendRoute`
  - [ ] keep Gateway connection/auth/server-list concerns in the generic SDK
  - [ ] keep descriptor JSON interpretation in Backend-specific SDK or game code
  - [ ] keep packet id/version/payload contracts documented or generated
  - [ ] preserve compatibility with both C# and C++ Backend implementations where applicable
- [ ] Add SDK integration expectations to Backend framework review guidance once real SDK code exists.

## Phase 9: Tests And Examples

- [ ] Add unit tests for route token ownership and raw route request/response matching.
- [ ] Add tests for stale server handle route-open rejection.
- [ ] Add tests for typed Backend SDKs receiving only an opened route abstraction.
- [ ] Add tests for descriptor JSON remaining opaque to the generic Gateway SDK.
- [ ] Add integration tests against a test Gateway route.
- [ ] Add sample code for:
  - [ ] connect/authenticate
  - [ ] list servers
  - [ ] open route by server handle
  - [ ] wrap route with a typed Backend SDK
  - [ ] send notify
  - [ ] send request and await response

## Open Questions

- Should the generic SDK be protocol-only, or should it include a default TCP/TLS transport implementation?
- Should Unity receive a thin package over a shared .NET SDK, or a Unity-specific SDK from the start?
- Should Backend-specific SDKs own their route lifetime, or should callers always close routes explicitly?
- How should inbound Backend-origin requests be represented in Unity-friendly APIs?
- Should the SDK expose raw packet APIs publicly, or keep them internal with an advanced escape hatch?
- What minimum platforms should the first SDK target support?
