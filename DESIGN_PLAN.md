# Design Plan

All plans in this file must be written in priority order, with the highest-priority design and safety work first. Completed implementation items should be removed from the active plan unless they are needed as short context for a remaining migration risk.

## P0: Gateway Route Hardening Status

### Scope

In-repository Gateway route hardening is implemented. Persistent Backend routing uses Gateway-issued `RouteToken` values, per-request `ExchangeId` values for route-data correlation, Backend session binding for Backend-origin authority checks, bounded client packet queues, route/exchange rate limits, and client idle/authentication timeouts.

The legacy one-shot `GATE_BACKEND_ROUTE` / client-provided `RouteId` pending flow has been removed from the client-facing Gateway path. Legacy requests now receive a rejection response instead of being registered or relayed.

The target behavior remains:

- `RouteToken` selects a Gateway-owned persistent Backend route.
- `ExchangeId` matches one request to one response within that route.
- Client-to-Backend and Backend-to-client exchanges are independent.
- Completing, timing out, or rejecting one exchange must not close the persistent route.
- Closing the route must cancel pending exchanges in both directions.

### Remaining Work

- No active in-repository Gateway route hardening work remains.

## Non-Goals

- Do not use Master as a data-plane relay.
- Do not make route tokens a substitute for authentication or authorization.
- Do not rely only on identifier entropy for security.
- Do not use one identifier for both persistent routing and per-request ACK correlation.
