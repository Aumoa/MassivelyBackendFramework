---
name: massivelybackend-pr-review
description: Repository-local pull request review guidance for MassivelyBackendFramework changes. Use when Codex reviews PRs, branch diffs, CI failures, architecture changes, security boundaries, or merge readiness in this repository.
---

# MassivelyBackendFramework PR Review

## Overview

MassivelyBackendFramework PR reviews should protect correctness, build health, security boundaries, and the repository's intended architecture without turning style preference into noise.

## Review Priorities

- Lead with actionable findings ordered by severity.
- Ground findings in concrete source locations, logs, commands, diffs, or protocol behavior.
- Separate blockers from residual risks and optional follow-up ideas.
- Understand the author's intended behavior and design before approving a pull request.
- Infer intent from the source, tests, pull request description, names, structure, comments, and surrounding implementation.
- If the intent of changed code cannot be inferred, ask the pull request author to explain it.
- Treat unresolved intent uncertainty as a merge-readiness blocker. Do not approve until the author explains the intent or the code is clarified enough to review its behavior.
- When a PR touches web-based services, ASP.NET services, Blazor UI, OAuth/OIDC work, API services, or frontend-related changes, also follow `WEBAGENTS.md`.
- When a PR touches high-performance socket server work, including Master, Gateway, Dedicated, channel execution, game networking, packet routing, or MMORPG server logic, also follow `SOCKETAGENTS.md`.

## Security Review

- Prioritize issues that could let a client gain, request, or exercise privileges beyond what the server explicitly authorizes.
- Treat client-to-server capability negotiation as security-sensitive. Clients may request capabilities, but the server must make the final authorization decision with least privilege and default-deny behavior.
- Flag risky defaults where client SDKs request powerful capabilities by default, especially remote control, file transfer, admin operations, payment, session, group, authorization, or data export capabilities.
- Do not treat client-provided roles, scopes, groups, permissions, or capability declarations as authoritative without server-side verification.
- For Master, Gateway, and Dedicated changes, verify that the changed behavior preserves the intended trust boundary: Gateway is the external security boundary, Master is the control plane, and Dedicated owns authoritative game state.

## GitHub And CI

- Use the current ordinary GitHub user account when submitting PR review feedback, approvals, change requests, or independent code-evaluation comments.
- Do not use GitHub App, bot, or integration credentials for review feedback that should appear as the current user's reviewer judgment.
- Use GitHub App or bot credentials only for author-side PR activity, such as replying to existing review feedback, explaining pushed commits, reporting validation results, or asking for re-review after the user requests that workflow.
- Before choosing credentials for remote validation, pull request updates, branch publication, or bot-authored review responses, follow `.codex/skills/github-app-credential-policy/SKILL.md`.
- Use `gh` proactively for read-only validation when available, especially pull request state, CI results, branch metadata, and review context.
- If `gh` or CI access is unavailable, state that remote validation was skipped and perform stricter source-level review.

## Merge Readiness

- Do not approve a PR while blockers remain unresolved, including unresolved intent uncertainty.
- Check whether the PR changes protected shared targets, release behavior, production-like configuration, or online workflows that require explicit final user approval before online changes.
- Treat temporary validation-only workflow, CI, or environment changes as blockers if they would affect protected branches after merge.
- For documentation-only or instruction-only PRs, verify the instruction location, trigger, and persistence rather than running unrelated builds.

## Review Comments

- Write external PR review comments in English unless the user asks otherwise.
- Explain the interpretation and recommendation to the user in Korean when the surrounding conversation is Korean.
- Clearly state whether the reviewed change is safe to merge, needs fixes first, or needs CI/runtime validation before judgment.
