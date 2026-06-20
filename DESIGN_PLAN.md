# Design Plan

All plans in this file must be written in priority order, with the highest-priority design and safety work first. Completed implementation items should be removed from the active plan unless they are needed as short context for a remaining migration risk.

## P0: Legacy RouteId Rollout

### Scope

In-repository Gateway route hardening is implemented. Persistent Backend routing now uses Gateway-issued `RouteToken` values, per-request `ExchangeId` values for route-data correlation, Backend session binding for Backend-origin authority checks, bounded client packet queues, route/exchange rate limits, and client idle/authentication timeouts.

The remaining work is rollout outside this repository: move any external callers away from the legacy one-shot `GATE_BACKEND_ROUTE` / client-provided `RouteId` flow.

The target behavior remains:

- `RouteToken` selects a Gateway-owned persistent Backend route.
- `ExchangeId` matches one request to one response within that route.
- Client-to-Backend and Backend-to-client exchanges are independent.
- Completing, timing out, or rejecting one exchange must not close the persistent route.
- Closing the route must cancel pending exchanges in both directions.

### Remaining Risks

- The legacy one-shot `GATE_BACKEND_ROUTE` flow is explicitly configurable for migration compatibility, but it remains enabled by default until external callers move to persistent route-open/data/close.

### Plan

1. Update any external clients to open persistent routes and use Gateway-issued `RouteToken` values.
2. Disable `EnableLegacyOneShotRoutes` in environments where all callers have migrated.
3. Keep compatibility tests while both paths exist.
4. Remove the old pending `RouteId` flow once all callers use route-open/data/close.

### Validation Plan

- New external clients do not need to provide authoritative route identifiers.
- Legacy callers continue to work during the migration window.
- Removing the legacy path does not remove persistent route-open/data/close coverage.

## Non-Goals

- Do not use Master as a data-plane relay.
- Do not make route tokens a substitute for authentication or authorization.
- Do not rely only on identifier entropy for security.
- Do not use one identifier for both persistent routing and per-request ACK correlation.
