# Backend Server Directory TODO

This document tracks future work for a client-visible Backend server directory.
The target design lets Backend/Dedicated nodes advertise game-specific server descriptor data,
and lets authenticated clients query that server list through Gateway before opening a route.

## Target Architecture

```text
Backend/Dedicated
  -> advertises BackendKind, routable node identity, endpoint, state, and opaque descriptor JSON
Master
  -> validates control-plane identity and distributes node directory snapshots
Gateway
  -> exposes client-safe server list views and opens routes to selected nodes
Client
  -> reads descriptor JSON as a game-specific contract and selects a server
```

Gateway and Master may store and forward descriptor JSON, but they should not interpret its
game-specific meaning. The descriptor is a contract between Backend/Dedicated and the client SDK/game.

## Design Principles

- Keep Backend/Dedicated endpoints private and never expose internal IP/port values to clients.
- Let Backend/Dedicated describe "what server I am" through opaque JSON.
- Keep descriptor JSON meaning outside Gateway and Master domain logic.
- Let Gateway decide the final routable Backend node, not the client.
- Use an opaque server handle for route open rather than trusting client-submitted descriptor fields.
- Keep Gateway responsible for authentication, visibility policy, route ownership, and route binding.
- Keep Dedicated responsible for game-specific channel/lane placement after the route reaches the selected server.
- Make the directory model work for both C# and C++ Backend nodes.

## Phase 1: Server Descriptor Contract

- [ ] Define a `BackendServerDescriptor` envelope for advertised server metadata.
- [ ] Include `BackendKind`, `NodeId`, `MasterConnectionId`, and node state outside opaque descriptor JSON.
- [ ] Add opaque `DescriptorJson` for game/client-specific display and selection data.
- [ ] Add `DescriptorVersion` and `DescriptorHash` for client/server drift detection.
- [ ] Define descriptor size limits per server entry.
- [ ] Require descriptor JSON to be valid UTF-8 JSON before Master accepts or distributes it.
- [ ] Forbid internal endpoint values, secrets, tokens, credentials, or private infrastructure identifiers in descriptor JSON.
- [ ] Decide whether descriptor JSON can be updated live or only during node advertisement/reconnect.

## Phase 2: Backend Advertisement

- [ ] Extend Backend endpoint advertisement with server descriptor data.
- [ ] Support C# Backend descriptor advertisement.
- [ ] Support C++ Backend descriptor advertisement through the sidecar.
- [ ] Validate descriptor size, JSON validity, and required envelope fields during Master control-plane handling.
- [ ] Add node state values suitable for server list display and routing:
  - [ ] open
  - [ ] full
  - [ ] draining
  - [ ] maintenance
  - [ ] offline or unavailable
- [ ] Decide whether load/capacity values are common envelope fields or remain inside opaque descriptor JSON.

## Phase 3: Master Directory Snapshot

- [ ] Add a Master-to-Gateway Backend server directory snapshot.
- [ ] Include enough routable identity for Gateway to map a listed server to a concrete Backend node.
- [ ] Include descriptor JSON and descriptor hash for client-visible listings.
- [ ] Broadcast snapshot updates when Backend nodes connect, disconnect, re-advertise, or change state.
- [ ] Add bounded cache/staleness rules for Gateway behavior during Master disconnects.
- [ ] Add Master admin/status visibility for advertised descriptors and descriptor hashes.

## Phase 4: Gateway Client Server List

- [ ] Add a Gateway client request for listing servers by `BackendKind`.
- [ ] Require authentication before returning server lists unless explicitly configured otherwise.
- [ ] Filter out nodes that are not client-visible or not routable.
- [ ] Return client-safe entries containing:
  - [ ] opaque `ServerHandle`
  - [ ] `BackendKind`
  - [ ] node state
  - [ ] descriptor version
  - [ ] descriptor hash
  - [ ] descriptor JSON
- [ ] Add paging or bounded result limits for large server lists.
- [ ] Add cache headers or freshness metadata appropriate for persistent socket protocols.
- [ ] Add rate limits for server list requests.

## Phase 5: Route Open By Server Handle

- [ ] Extend route-open requests to accept an opaque `ServerHandle`.
- [ ] Bind `ServerHandle` to a specific Backend node identity known by Gateway.
- [ ] Reject route-open requests with stale, unknown, unauthorized, full, draining, or maintenance handles.
- [ ] Keep supporting `BackendKind`-only auto-selection for simple clients and tests.
- [ ] Decide whether a route-open response should echo descriptor hash/version for the selected server.
- [ ] Ensure route binding records the selected `NodeId`, `MasterConnectionId`, and direct connection id.

## Phase 6: Client SDK Support

- [ ] Add generic SDK APIs for server listing:
  - [ ] list by `BackendKind`
  - [ ] inspect descriptor JSON
  - [ ] open route by `ServerHandle`
- [ ] Keep descriptor JSON parsing in game-specific SDK/application code.
- [ ] Expose descriptor hash/version to help clients detect mismatched server contracts.
- [ ] Avoid exposing internal Backend endpoints through SDK models.
- [ ] Support reconnect flows where a reconnect ticket can resolve back to the prior server.

## Phase 7: Compatibility And Tests

- [ ] Add C# Backend tests for descriptor advertisement.
- [ ] Add C++ sidecar tests for descriptor advertisement.
- [ ] Add Master snapshot tests for descriptor validation and distribution.
- [ ] Add Gateway server list tests for filtering, paging, and auth requirements.
- [ ] Add route-open-by-handle tests.
- [ ] Add stale handle and disconnected node rejection tests.
- [ ] Add tests that descriptor JSON remains opaque to Gateway routing logic.
- [ ] Add cross-language descriptor examples for C# and C++ Backend implementations.

## Open Questions

- Should load and capacity be common routing fields, descriptor JSON fields, or both?
- Should Gateway support server-side sorting/filtering only by common fields, leaving descriptor filtering to clients?
- How long should server handles remain valid after a directory snapshot changes?
- Should descriptors be signed or hash-validated against a Master/Admin-approved schema?
- Should clients receive all descriptors for a `BackendKind`, or should a separate Lobby/Directory Backend handle large game-specific searches?
- How should reconnect tickets interact with server handles and node restarts?
