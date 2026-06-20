# Design Plan

All plans in this file must be written in priority order, with the highest-priority design and safety work first. Completed implementation items should be removed from the active plan unless they are needed as short context for a remaining migration risk.

## P0: Remaining Gateway Route Hardening

### Scope

Persistent Backend routing now uses Gateway-issued `RouteToken` values and per-request `ExchangeId` values for route-data correlation. The remaining hardening work is to finish the parts that still depend on broader runtime ownership, Backend session lifetime, and migration away from the legacy one-shot `RouteId` flow.

The target behavior remains:

- `RouteToken` selects a Gateway-owned persistent Backend route.
- `ExchangeId` matches one request to one response within that route.
- Client-to-Backend and Backend-to-client exchanges are independent.
- Completing, timing out, or rejecting one exchange must not close the persistent route.
- Closing the route must cancel pending exchanges in both directions.

### Remaining Risks

- Persistent routes are currently bound to an authenticated client connection and Backend kind, but not yet strongly pinned to a specific Backend node/session lifetime.
- If a Backend session is replaced or lost, the Gateway still needs an explicit policy for closing, suspending, or rebinding affected persistent routes.
- Backend-origin route close is not yet wired as an explicit data-plane signal to clients.
- Abuse controls exist for open route counts and pending exchanges, but route-open/exchange creation rate limiting and bounded client input queues are still missing.
- The legacy one-shot `GATE_BACKEND_ROUTE` flow still accepts client-provided `RouteId` values and must remain treated as a migration compatibility path, not an authoritative persistent route model.

## P1: Backend-Initiated Route Close

### Protocol Plan

1. Allow trusted Backend sessions to send `GATE_BACKEND_ROUTE_CLOSE`.
   - Decode `GatewayBackendRouteClose` from Backend sessions.
   - Require the Backend-to-Gateway frame to use the expected close packet kind and protocol version.
   - Raise the close frame through `BackendConnectionManager` to `ConnectionManager`.

2. Close the persistent route in Gateway.
   - Verify the route token is known and open.
   - Verify the route is bound to the Backend kind, and later to the exact Backend node/session once node binding is implemented.
   - Close the route and clear pending exchanges.

3. Notify the owning client.
   - Forward a close notification to the route owner.
   - Do not let Backend close requests affect routes owned by other Backend kinds or unknown tokens.
   - Default-deny malformed, unknown, mismatched, or stale close frames.

### Validation Plan

- Backend close for an open route notifies the owning client and removes the route.
- Backend close clears client-origin and Backend-origin pending exchanges.
- Backend close with an unknown route token is ignored.
- Backend close from the wrong Backend kind is rejected.
- Client data sent after Backend close is not relayed.

## P2: Backend Node And Session Binding

### Plan

1. Extend persistent route state beyond `BackendKind`.
   - Track the selected Backend node id and master connection id, or an explicit Backend session identity.
   - Decide whether route-open should immediately allocate/connect to a Backend session or lazily bind on first relay.

2. Validate every Backend-origin frame against the route binding.
   - Match Backend kind.
   - Match Backend node/session identity once available.
   - Reject frames from a replacement or unrelated Backend session unless a deliberate rebinding policy exists.

3. Handle Backend session loss.
   - Close or suspend routes bound to the lost session.
   - Clear pending exchanges.
   - Notify clients when the route is no longer usable.

### Validation Plan

- Backend frame from the wrong node/session is rejected.
- Backend session loss closes or suspends affected routes.
- Session replacement does not inherit old route authority by Backend kind alone.

## P3: Abuse Controls

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

## P4: Legacy RouteId Migration

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
