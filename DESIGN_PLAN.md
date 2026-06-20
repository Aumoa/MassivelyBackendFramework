# Design Plan

All plans in this file must be written in priority order, with the highest-priority design and safety work first. Completed implementation items should be removed from the active plan unless they are needed as short context for a remaining migration risk.

## P0: Remaining Gateway Route Hardening

### Scope

Persistent Backend routing now uses Gateway-issued `RouteToken` values, per-request `ExchangeId` values for route-data correlation, and Backend session binding for Backend-origin authority checks. The remaining hardening work is to finish broader abuse controls and migration away from the legacy one-shot `RouteId` flow.

The target behavior remains:

- `RouteToken` selects a Gateway-owned persistent Backend route.
- `ExchangeId` matches one request to one response within that route.
- Client-to-Backend and Backend-to-client exchanges are independent.
- Completing, timing out, or rejecting one exchange must not close the persistent route.
- Closing the route must cancel pending exchanges in both directions.

### Remaining Risks

- Abuse controls exist for open route counts, pending exchanges, connection-scoped route-open bursts, and per-route exchange creation bursts, but bounded client input queues, idle/authentication timeouts, and principal-scoped limits are still missing.
- The legacy one-shot `GATE_BACKEND_ROUTE` flow is now explicitly configurable for migration compatibility, but it remains enabled by default until callers move to persistent route-open/data/close.

## P1: Abuse Controls

### Plan

- Add bounded client input queues or equivalent backpressure for externally reachable client sockets.
- Add principal-scoped route-open, open-route, and exchange creation limits once authenticated principal identity is modeled.
- Consider per-connection exchange creation limits across all routes if route fan-out can bypass per-route limits.
- Add idle/authentication timeouts where they are not already explicit.

### Validation Plan

- Backpressure does not create unobserved fire-and-forget failures during shutdown.
- Principal-scoped limits apply across reconnects or multiple simultaneous connections for the same authenticated actor.
- Idle or unauthenticated clients are disconnected without affecting authenticated active clients.

## P2: Legacy RouteId Migration

### Plan

1. Update clients to open persistent routes and use Gateway-issued `RouteToken` values.
2. Disable `EnableLegacyOneShotRoutes` in environments where all callers have migrated.
3. Keep compatibility tests while both paths exist.
4. Remove the old pending `RouteId` flow once all callers use route-open/data/close.

### Validation Plan

- New clients do not need to provide authoritative route identifiers.
- Legacy callers continue to work during the migration window.
- Removing the legacy path does not remove persistent route-open/data/close coverage.

## Non-Goals

- Do not use Master as a data-plane relay.
- Do not make route tokens a substitute for authentication or authorization.
- Do not rely only on identifier entropy for security.
- Do not use one identifier for both persistent routing and per-request ACK correlation.
