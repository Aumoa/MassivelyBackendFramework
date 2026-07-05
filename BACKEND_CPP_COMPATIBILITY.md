# C# And C++ Backend Compatibility Policy

This document defines the compatibility boundary for Backend framework features once
C++ Backend/Dedicated server support is available.

## Compatibility Model

C# Backend and C++ Backend are peer implementations of the Backend runtime contract.
New Backend framework work must be classified before it changes Master, Gateway,
Backend, sidecar, PacketCore, or packet manifest behavior:

- Shared feature: implement the behavior in both C# and C++, or implement it once in
  a shared wire contract that both runtimes consume.
- C# implementation detail: keep the behavior behind the C# runtime boundary and
  document why C++ nodes do not need to observe it.
- C++ implementation detail: keep the behavior behind the C++ runtime boundary and
  document why C# nodes do not need to observe it.
- Deferred parity: document the boundary, the unsupported runtime behavior, and the
  compatibility risk before merging the first implementation.

Runtime-specific behavior must not leak into Master or Gateway authorization decisions
unless the runtime has explicitly advertised a protocol version, transport flag, or
manifest capability that the server side validates.

## Feature Change Rule

When adding a Backend framework feature in C#:

- Add the C++ equivalent when the feature changes Gateway-facing packets, Master-facing
  control packets, sidecar local-control packets, manifest semantics, endpoint
  discovery, shutdown, status, or route policy behavior.
- If C++ parity is not practical in the same change, document the compatibility
  boundary in the feature documentation or pull request and keep the default behavior
  safe for C++ Backend nodes.
- Add or update cross-language wire vectors for shared PacketCore payloads.

When adding a Backend framework feature in C++:

- Add the C# equivalent when the feature changes a shared wire contract, shared
  manifest data, Gateway routing behavior, or Master admin/status behavior.
- If C# parity is not practical in the same change, document the compatibility
  boundary and keep C# Backend nodes on the existing behavior without degraded
  authorization, routing, or status semantics.
- Prefer extending existing C# test fixtures with matching C++ vectors instead of
  relying on C++-only tests for shared behavior.

## Rolling Protocol Changes

Shared wire contracts must remain compatible during rolling deployments:

- Add fields or packet ids before removing or repurposing existing fields.
- Preserve existing packet ids, enum values, and payload field order.
- Bump protocol or payload versions when old readers cannot safely ignore a change.
- Ship readers that accept old and new forms before writers require the new form.
- Keep authorization server-side and default-deny when a version or capability is
  missing, unknown, or malformed.
- Update `BACKEND_CPP_PROTOCOL.md` and cross-language vector tests in the same change
  that changes a shared PacketCore contract.

## Manifest And Schema Sources

Packet manifests and schema metadata are the preferred shared source for packet
compatibility. When practical, schema or manifest inputs should generate both C# and
C++ parser metadata instead of requiring hand-maintained copies.

If generation is not practical for a feature, the change must still keep these sources
aligned:

- The approved Master manifest snapshot is the compatibility authority.
- Gateway verifier policy uses backend kind, manifest id, and manifest hash, not the
  Backend runtime language.
- C++ sidecar snapshot reads must use the same manifest snapshot wire codec as C#.
- C# and C++ vector tests must pin any shared codec change.

## Master And Gateway Neutrality

Master and Gateway must avoid C#-only assumptions that would block C++ Backend nodes:

- Treat backend kind, endpoint, transport flags, manifest id, and manifest hash as
  advertised runtime metadata, not .NET type identity.
- Do not require the Backend process to host the Gateway listener when the sidecar is
  configured to advertise a C++ data-plane endpoint.
- Do not assume Master-facing admin/status details are produced by the Backend process
  itself; the sidecar may own Master-facing status while consuming C++ snapshots.
- Authorize Gateway direct sessions by direct-connect code validation and manifest
  policy, not by trusting client-provided runtime labels.

The C++ path must avoid assumptions that make C# Backend nodes second-class:

- Do not require C++-specific endpoint readiness, runtime status, or manifest snapshot
  packets for ordinary C# Backend nodes.
- Keep Gateway channel frames compatible with the existing C# Backend data-plane
  behavior.
- Keep sidecar-only coordination off the gameplay packet hot path so C# direct Backend
  routing keeps its current performance model.

## Current Decisions

- The first implementation uses a C# sidecar for Master control-plane integration.
- Local sidecar communication uses TCP loopback PacketCore control frames.
- The sidecar owns Master-facing admin/status, while C++ provides endpoint readiness,
  runtime status, shutdown state, and manifest snapshots through local control packets.
- If the sidecar is connected to Master but the C++ listener is unhealthy, the sidecar
  reports the endpoint as not ready and does not advertise an unreachable data-plane
  endpoint when readiness is required.
- Gateway route-open responses must not expose C++-specific runtime capability data to
  clients. Runtime and manifest metadata stay in Master/Gateway policy and admin/status
  surfaces.

## Review Checklist

- Does this change alter a PacketCore payload, packet id, enum value, or version?
- Does it require both C# and C++ readers or writers?
- Are old and new nodes safe during rolling deployment?
- Are backend kind, manifest id, manifest hash, and transport flags treated as metadata
  instead of authorization secrets?
- Are Gateway verifier decisions independent of Backend runtime language?
- Are cross-language vector tests updated for shared wire changes?
