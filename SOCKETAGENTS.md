# High-Performance Socket Server Instructions

## Architecture

- Socket server work targets a high-performance real-time server design that is distinct from the ASP.NET/Web API service model.
- The socket server architecture is divided into Master, Gateway, and Dedicated services.
- Master is the control plane. It observes and provides Gateway and Dedicated connection state, service locations, channel ownership, and external dependency status such as MySQL or Redis.
- Master provides connection and ownership information only. Do not use Master as a relay server or data plane for actual game packets.
- Gateway is a lightweight data relay/proxy between clients and Dedicated servers. It handles client authentication, authorization checks, session management, packet validation, and Dedicated routing.
- Gateway should be horizontally scalable, similar to a high-performance web service. Gateway must not process game logic or authoritative game state.
- Dedicated is the authoritative server where core game logic runs. Final game-state decisions and mutations happen in Dedicated.
- Dedicated primarily targets MMORPG workloads where performance is extremely important.

## Master Design

- The long-term Master goal is a Kubernetes-like control plane that can scale to multiple instances.
- A staged approach is acceptable, including a single Master, active-standby Master, or lease-based failover.
- Master failure must not immediately terminate existing game sessions.
- Gateway and Dedicated should be able to cache routing and ownership information from Master for a bounded time or hold it through a lease model.
- Model channel ownership explicitly, such as `ChannelId -> Owner Dedicated`, so Gateway can route clients to the correct Dedicated server.

## Trust Boundary And Packet Validation

- Gateway is the external security boundary. Client-facing Gateway sockets should use TLS/SSL, perform authentication and authorization, apply rate limiting or other DoS controls, and reject malformed or incompatible packets before forwarding them inward.
- Master and Dedicated are internal services. They rely on platform isolation such as private networking, firewalls, security groups, or Kubernetes network policies to prevent direct external access.
- Master and Dedicated may trust packets that arrive from authenticated internal nodes and skip expensive hostile-input checks in hot paths, but they must keep cheap protocol sanity checks such as packet header, kind, id, version, payload length, and schema compatibility.
- Gateway owns basic packet compatibility validation, including version and payload size policy. Dedicated owns game-rule validation and final authoritative state changes.
- Treat packet headers, magic values, opcodes, packet kind IDs, and client build IDs as framing or compatibility data, not as secrets or proof that the sender is an authorized client.
- Assume hostile clients can learn and replay any constants shipped in the client binary or visible on the wire. Reviews should flag designs that rely on obscured packet IDs, cracked HEAD values, or similar client-known identifiers as an anti-forgery mechanism.
- Keep packet boundary detection separate from authentication, authorization, and session validation. A well-formed packet with a valid header or kind must still be checked against the authenticated connection, current session state, allowed packet set, payload schema, and routing authority before it is forwarded or applied.
- For state-changing or order-sensitive protocols, review whether the server needs sequence numbers, nonces, replay windows, idempotency keys, or other cheap checks to reject replayed, reordered, duplicated, or cross-session packets.
- Do not use Master as a data-plane relay for player gameplay packets. Master should observe and coordinate service state, ownership, and routing metadata only.

## Gateway Batching

- Gateway normally forwards packets immediately.
- Gateway batching is a defensive optimization used when network sends become a bottleneck and packets accumulate in the send queue before the next send.
- Small and frequent packets, such as movement packets, may be grouped under a shared protocol header when queued together.
- Do not casually batch packets that require immediate handling or strict correctness, such as authentication, authorization, payment, session control, or important combat input.

## Gateway Relay Performance

- Gateway is the only client-facing path to internal Backend and Dedicated services, so Gateway relay design must treat throughput, latency, jitter, allocation rate, and backpressure as first-order concerns.
- Gateway is horizontally scalable, but scale-out does not remove per-packet latency from the relay path. Avoid unnecessary payload copies, allocations, locks, scheduler hops, and packet re-encoding work even when more Gateway instances can be added.
- Prefer parsing Gateway envelopes and routed payload boundaries with `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>`, pooled buffers, or pipeline-style readers/writers when those APIs can reduce allocations or copies.
- Keep routed payloads as slices or borrowed memory through the Gateway hot path where practical. Do not decode game payload semantics in Gateway unless a Gateway-owned compatibility or verifier policy requires it.
- If safe managed APIs cannot reasonably meet Gateway hot-path goals, narrowly scoped `unsafe` code may be used when the expected performance benefit is substantial and the buffer lifetime, bounds, pinning, alignment, and concurrency assumptions are explicit.
- Do not add `unsafe` code or ref-heavy complexity for minor, speculative, or cold-path wins. Prefer measured evidence or clear hot-path reasoning before accepting the maintenance and memory-safety cost.

## Dedicated Execution Model

- Treat each game channel as a single logical execution flow owned by a channel owner lane.
- Game state mutations must happen only on the owning channel lane.
- Parallelizable work may use TPL, worker pools, background workers, or similar mechanisms.
- Worker code must not directly mutate world state or entity state.
- Pass required data to workers as snapshots, commands, or immutable data, then post results back to the channel mailbox.
- Keep the central rule: mutate game state only on the channel owner lane, and return external or parallel work results through messages.

## .NET Performance Policy

- Use .NET for productivity, but apply .NET performance optimization aggressively in Gateway relay paths and Dedicated hot paths.
- Use Dependency Injection for server composition, service wiring, and lifetime management.
- Do not use Dependency Injection as part of real-time packet processing or game-logic hot paths.
- In hot paths, minimize allocation, locks, blocking calls, scheduler overhead, reflection, and unnecessary async state machine creation.
- Consider high-performance .NET primitives when appropriate, including `System.IO.Pipelines`, `SocketAsyncEventArgs`, `ArrayPool<T>`, `MemoryPool<T>`, `Span<T>`, `Memory<T>`, `ValueTask`, bounded queues, and custom schedulers.
- Make a serious effort to improve throughput, latency, allocation behavior, and cache locality in performance-sensitive socket and Dedicated paths. When safe abstractions cannot reasonably deliver the needed result, `ref`, `in`, `readonly ref`, `ref struct`, `stackalloc`, custom memory layouts, or `unsafe` code may be appropriate.
- Keep `unsafe` and ref-heavy code narrowly scoped, isolated behind clear APIs, and justified by a meaningful measured or clearly reasoned performance benefit. Do not introduce unsafe contexts or hard-to-maintain low-level code for speculative or cold-path wins.
- When writing `unsafe` or ref-heavy code, keep its assumptions and failure modes bounded enough for the author to reason about completely. Input ranges, buffer lengths, pointer lifetimes, pinning, alignment, aliasing, ownership, and mutation concurrency should be explicit before entering the unsafe or low-level section.
- During reviews, actively examine performance-sensitive changes for avoidable allocations, copies, bounds checks, synchronization, blocking, scheduler overhead, and data-layout issues. Be practical rather than dogmatic: prefer clear evidence or hot-path reasoning, and avoid nitpicking low-impact cold paths.
- During reviews of `unsafe` or ref-heavy code, verify that the relevant failure modes are enumerable, testable where practical, and not dependent on unclear external input, lifetime, ownership, or concurrency behavior. Push back when the reviewer cannot reasonably predict memory-safety behavior from local invariants and documented preconditions.

## Async Work And SynchronizationContext

- Dedicated may call web APIs or external services, but the main game tick must not wait on those responses.
- Avoid unmanaged fire-and-forget patterns where exceptions are unobserved or shutdown cannot track pending work.
- Async branches should be managed with whole-operation try-catch, timeout, cancellation, logging, and shutdown tracking.
- A channel `SynchronizationContext` may post continuations back to the original channel mailbox.
- Apply results from external async work only after returning to the channel's safe execution flow.

## Backend Web Services

- Ranking, user data, inventory, payment, logging, analytics, and operations APIs may be implemented as separate web services.
- Dedicated may use those APIs, but it must not block the game main tick while waiting for web API responses.
- Prefer event-driven processing, async workers, cached snapshots, and eventual consistency when exact immediate results are not required by gameplay.

## Dedicated Server Boundaries

- When multiple Dedicated servers exist, real-time interaction across different Dedicated servers is not supported by default.
- Server migration or state copying may move a player from server A to server B to create a continuous user experience.
- Without an explicit migration process, avoid designs where multiple Dedicated servers share or interfere with one real-time game state.
- If load increases, split ownership by channel or world area so a fully independent Dedicated server can own that slice.
