# Backend Packet Manifest TODO

This document tracks future work for a Gateway-side routed packet validation model.
The target design is the hybrid manifest model:

- Backend binaries or configs declare a packet manifest id and hash.
- Master/Admin stores the approved manifest definitions.
- Backend nodes advertise only the active manifest id/hash during control-plane registration.
- Master validates the advertised manifest and distributes the approved packet policy to Gateways.
- Gateways validate routed packet contracts before relaying payloads to Backend nodes.

The goal is to let Gateway reject structurally invalid or unauthorized routed packets without
making Gateway own game-specific business rules.

## Design Principles

- Keep Gateway as the client-facing security boundary.
- Keep Backend/Dedicated as the owner of authoritative gameplay validation.
- Do not let Backend self-advertised packet definitions become authoritative by themselves.
- Treat `PacketId`, `PacketKind`, protocol version, schema hash, and payload length as compatibility data, not secrets.
- Prefer explicit wire schemas over native C++ struct memory layouts.
- Allow Gateways to cache manifest snapshots and continue operating for a bounded time during Master outages.
- Keep validation cost predictable enough for Gateway scale-out workloads.

## Phase 1: Contract Model

- [ ] Define `BackendPacketManifestId` and `BackendPacketManifestHash` value rules.
- [ ] Define a `BackendPacketManifest` model keyed by `BackendKind`, manifest id, and hash.
- [ ] Define packet entries keyed by direction, `PacketKind`, `PacketId`, and routed version.
- [ ] Add per-entry payload constraints:
  - [ ] minimum payload length
  - [ ] maximum payload length
  - [ ] exact fixed payload length when applicable
  - [ ] optional structural schema id/hash
- [ ] Decide whether Backend-to-client packet constraints are enforced by Gateway, logged only, or deferred.
- [ ] Decide how deprecated packet versions remain accepted during rolling deployments.

## Phase 2: Master/Admin Source Of Truth

- [ ] Add a Master/Admin management API for approved Backend packet manifests.
- [ ] Persist approved manifests in Master storage.
- [ ] Add create/update/deprecate/remove operations with audit-friendly metadata.
- [ ] Validate that manifest hash matches the stored manifest content.
- [ ] Prevent duplicate active manifests for the same `BackendKind` unless rollout rules explicitly allow them.
- [ ] Add status visibility showing which Backend nodes run which manifest id/hash.

## Phase 3: Backend Advertisement

- [ ] Extend Backend endpoint advertisement to include manifest id and hash.
- [ ] Reject Backend registration when the advertised manifest is not approved for its authorized `BackendKind`.
- [ ] Include manifest information in Backend node snapshots sent to Gateways.
- [ ] Support rolling deployments where multiple approved manifests may be live for one `BackendKind`.
- [ ] Decide whether manifest changes require reconnect, re-advertise, or node restart.

## Phase 4: Gateway Snapshot And Cache

- [ ] Add a Master-to-Gateway packet manifest policy snapshot.
- [ ] Publish snapshots to Gateway during discovery and whenever policy changes.
- [ ] Cache packet policies by `BackendKind`, manifest id, and hash.
- [ ] Associate each trusted Backend direct session with the manifest approved by Master.
- [ ] Add bounded staleness rules for Gateway cache behavior during Master disconnects.
- [ ] Expose Gateway status for loaded manifests and last policy update time.

## Phase 5: Gateway Routed Packet Validation

- [ ] Validate client-to-Backend routed packets against the active Backend session manifest.
- [ ] Reject unknown `PacketId` and routed version combinations.
- [ ] Reject packet kinds that do not match the manifest entry.
- [ ] Reject payloads outside manifest length constraints.
- [ ] Keep existing route-token ownership, direction, and exchange-id checks.
- [ ] Add counters for rejected packets by reason without logging every malformed packet.
- [ ] Apply rate limiting or route closure policy for repeated structural violations.

## Phase 6: Optional Structural Schema Verification

- [ ] Choose a schema representation for structural wire validation.
- [ ] Prefer a compact schema format that can be interpreted without allocations on hot paths.
- [ ] Support fixed-width primitive fields, byte blobs, strings, arrays, and nested records only when needed.
- [ ] Add a schema verifier that validates boundaries without constructing full game objects.
- [ ] Benchmark schema validation cost against Backend-side decoding cost.
- [ ] Enable structural verification per packet entry instead of globally for every routed packet.

## Phase 7: Backend Runtime Contract

- [ ] Document which Gateway checks Backend can rely on for trusted Gateway sessions.
- [ ] Keep cheap Backend sanity checks for packet kind, id, version, and payload length.
- [ ] Keep all gameplay and authorization decisions in Backend/Dedicated.
- [ ] Add compatibility guidance for C++ Backend wire schemas.
- [ ] Provide a generated or shared manifest source so Backend code and Master/Admin policy cannot drift silently.

## Phase 8: Tests And Validation

- [ ] Add unit tests for manifest hash calculation and approval rules.
- [ ] Add unit tests for Gateway packet policy matching.
- [ ] Add malformed packet tests for unknown packet id, wrong version, wrong kind, oversized payload, and undersized payload.
- [ ] Add rolling deployment tests with multiple approved manifests for one `BackendKind`.
- [ ] Add integration tests for Backend advertise -> Master approval -> Gateway snapshot -> route data validation.
- [ ] Add performance tests for hot-path validation with representative packet rates.

## Open Questions

- Should Gateway enforce Backend-to-client packet manifests, or only verify client-to-Backend traffic?
- Should manifest policy live entirely in Master storage, or be generated from checked-in schema files?
- How much structural validation is worth doing before the cost approaches full decode?
- What is the rollback behavior when a Backend node advertises a manifest that was approved and then revoked?
- Should route-open responses include the manifest id/hash selected for the route?
- Should clients know manifest ids, or should that remain an internal Gateway/Backend detail?
