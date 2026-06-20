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

- Abuse controls exist for open route counts and pending exchanges, but route-open/exchange creation rate limiting and bounded client input queues are still missing.
- The legacy one-shot `GATE_BACKEND_ROUTE` flow still accepts client-provided `RouteId` values and must remain treated as a migration compatibility path, not an authoritative persistent route model.

## P1: Abuse Controls

### Plan

- Add bounded client input queues or equivalent backpressure for externally reachable client sockets.
- Add route-open rate limits per connection and per authenticated principal.
- Add exchange creation rate limits per route, direction, connection, and authenticated principal.
- Add idle/authentication timeouts where they are not already explicit.
- Add open route limits per authenticated principal.

### Validation Plan

- Route-open burst attempts are limited before contacting Backend nodes.
- Exchange creation bursts are limited without closing unrelated exchanges.
- Backpressure does not create unobserved fire-and-forget failures during shutdown.

## P2: Legacy RouteId Migration

### Plan

1. Update clients to open persistent routes and use Gateway-issued `RouteToken` values.
2. Mark the legacy one-shot `GATE_BACKEND_ROUTE` / client-provided `RouteId` path as deprecated.
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
