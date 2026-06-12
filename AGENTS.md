# Codex Workflow Rules

## Domain-Specific Instructions

- Before starting domain-specific work, read and follow the matching instruction file below.
- For web-based services, ASP.NET services, Blazor UI, OAuth/OIDC work, API services, and frontend-related changes, also follow `WEBAGENTS.md`.
- For high-performance socket server work, including Master, Gateway, Dedicated, channel execution, game networking, packet routing, and MMORPG server logic, also follow `SOCKETAGENTS.md`.
- If a task touches both domains, follow both files and prefer the stricter runtime, safety, or performance rule where they conflict.

## Skill Extraction Policy

- When new or updated instructions would work better as a reusable skill, separate and store them as a skill instead of expanding `AGENTS.md` indefinitely.
- Only extract instructions into a skill when the skill has a clear trigger, reusable workflow, or meaningful context-saving benefit.
- When extracting instructions into a skill, tell the user that the instructions were separated and where they were stored.

## Project Classification Skill

- When adding, moving, or classifying projects in the solution, read and follow `.codex/skills/classify-solution-project/SKILL.md`.

## Pull Request Review Skill

- When reviewing pull requests, branch diffs, CI failures, architecture changes, or merge readiness, read and follow `.codex/skills/massivelybackend-pr-review/SKILL.md`.

## Coding Style Policy

- Follow Microsoft's standard C# coding conventions by default.
- Keep repository-specific deviations from the standard documented only under `Coding Style Exceptions`.

## Coding Style Exceptions

- Prefix private C# member fields with `m_`.

## Pull Request Review Policy

- Use the current ordinary GitHub user account when submitting pull request review feedback that evaluates code, opens new review findings, approves, or requests changes.
- Do not use GitHub App, bot, or integration credentials for review feedback that should appear as the current user's reviewer judgment. If suitable current-user credentials are unavailable, report the review findings to the user instead of posting them remotely.
- GitHub App or bot credentials may be used for author-side PR activity, such as replying to existing review feedback, explaining pushed commits, updating PR descriptions, reporting validation results, or asking for re-review after the user requests that workflow.
- Before approving a pull request, understand the intent of the changed code.
- Infer intent from the source, tests, pull request description, names, structure, comments, and surrounding implementation.
- If the intent cannot be inferred, ask the pull request author to explain it.
- Treat unresolved intent uncertainty as a merge-readiness blocker, and do not approve the pull request until the author explains the intent or the code is clarified enough to review its behavior.
- When reviewing pull requests, prioritize issues that could let a client gain, request, or exercise privileges beyond what the server explicitly authorizes.
- Treat client-to-server capability negotiation as security-sensitive. Clients may request capabilities, but the server must make the final authorization decision with least privilege and default-deny behavior.
- Flag risky defaults where client SDKs request powerful capabilities by default, especially remote control, file transfer, admin operations, payment, session, group, authorization, or data export capabilities.
- Do not treat client-provided roles, scopes, groups, permissions, or capability declarations as authoritative without server-side verification.

## Online Change Approval Policy

- Treat `dev`, `master`, `main`, release branches, production branches, and any branch or environment used by other users as protected shared targets.
- Always get final user approval before operations that publish, push, deploy, release, upload, create or update remote pull requests, or otherwise change online state on protected shared targets or production-like environments.
- If final approval cannot be requested or received for a protected shared target, do not perform the online operation.
- Branches that are clearly isolated work branches, such as `codex/*`, may use a more flexible approval model for pushing, draft pull request updates, and other collaboration or validation tasks when doing so is useful for the requested work.
- When choosing credentials for GitHub commit-adjacent or online operations such as push, pull request creation or updates, branch publication, or remote validation, read and follow `.codex/skills/github-app-credential-policy/SKILL.md`.
- Even on work branches, avoid destructive remote operations, production-impacting changes, or changes that can affect other users without explicit user approval.
- When an online operation is blocked by missing approval, re-check the written code and local changes as thoroughly as practical to identify real issues before reporting back.
- Use GitHub-related tooling such as `gh` proactively for validation when available, especially read-only checks for pull request state, CI results, branch metadata, and review context.
- GitHub or `gh` operations that change protected shared targets or production-like online state still require final user approval.

## Git Commit Policy

- When implementing a requested feature, split the work into meaningful feature-sized commits.
- Create one commit per independently reviewable feature unit.
- Do not mix unrelated refactors, formatting, dependency updates, or bug fixes into the same commit unless they are required for that feature.
- Before committing, run the relevant build or test command when it is known and practical.
- Unless the user explicitly asks not to commit, create the commit after requested changes pass the relevant build or tests.
- If the relevant build or tests fail, do not commit until the failure is fixed or the user explicitly asks to commit anyway.
- If validation cannot be run, mention that in the final response.
- Do not commit user-made unrelated changes.
- If the working tree already contains unrelated changes, isolate only Codex-made changes in the commit.
- If a clean feature-sized commit is not possible, stop and explain why.
- For commits intended to participate in a GitHub App-authenticated isolated work-branch workflow, follow `.codex/skills/github-app-credential-policy/SKILL.md` before creating the commit identity.

## Commit Message Format

- Commit subjects must use this exact format: `Codex: Commit Message`.
- Replace `Commit Message` with a concise imperative summary of the feature or change.
- Keep the subject under 72 characters when practical.
- Optionally include a short commit body with 1-3 bullet points describing what changed and any validation result.

## Commit Examples

```text
Codex: Add OAuth login callback handling

- Add callback endpoint and token exchange wiring.
- Validate with dotnet build.
```

```text
Codex: Fix Discord command registration

- Update registration flow to avoid duplicate commands.
- Tests not run; no targeted test project found.
```
