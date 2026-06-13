---
name: classify-solution-project
description: Classify MassivelyBackendFramework projects into solution folders. Use when adding, moving, or reviewing projects in the solution, especially when choosing between Frontend, Backend, Services, Utility, and Tests.
---

# Classify Solution Project

## Classification Rules

- Place projects in the solution folder that matches their runtime boundary and primary responsibility.
- Use `Frontend` for web servers or any server that directly accepts external connections.
- Use `Backend` for servers that communicate only with internal services.
- Use `Services` for projects that define service components, contracts, abstractions, controllers, core logic, SQL, or other service-owned building blocks.
- Use `Utility` for projects that simply provide reusable functionality to many services without owning a service domain.
- Use `Tests` for test projects, including unit, integration, protocol, parser, math, validation, and regression test projects. Create the `Tests` solution folder if it is missing when adding a test project.

## Decision Flow

- If the project accepts browser, client, webhook, public API, socket, or other external traffic directly, classify it as `Frontend`.
- If the project runs as a service host but only communicates with internal services, classify it as `Backend`.
- If the project defines a service's owned components or implementation pieces without being the external/internal host itself, classify it as `Services`.
- If the project only provides reusable helpers, shared primitives, extensions, or infrastructure utilities across many domains, classify it as `Utility`.
- If the project primarily contains automated tests or test fixtures, classify it as `Tests` regardless of which production project it targets.
- If a project appears to fit multiple folders, choose the folder that represents the project's primary runtime boundary and note the tradeoff to the user.
