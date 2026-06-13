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

## API Surface

- Check whether newly added or widened `public` and `protected` members are intentionally part of an API surface or extension point.
- Prefer the narrowest accessibility that supports the current design.
- Flag unnecessary `public` or `protected` exposure when the member is only an implementation detail or can reasonably stay private/internal.
- Allow `public` or `protected` members without current call sites when the code clearly defines an appropriate API, contract, override point, or future extension point.
- Do not request narrower accessibility solely because a well-scoped API has no current in-repository caller.

## Class Responsibility

- Check whether each class has one clear primary responsibility.
- Flag classes that combine unrelated responsibilities when the coupling makes behavior harder to reason about, test, extend, or review.
- Accept responsibility mixing only when there is a clear optimization, platform constraint, hot-path requirement, compatibility concern, or similarly hard-to-avoid reason.
- Prefer moving behavior into cohesive collaborators when it reduces coupling without adding needless indirection.
- Do not request extra class splitting when the existing responsibility boundary is already understandable and further separation would mostly add ceremony.

## Function Extraction

- Review helper-method extraction by the meaning of the behavior, not by line count alone.
- Avoid asking for one-off helper methods for trivial one- or two-line behavior when the extracted method has no distinct domain meaning and is only used in one fixed location.
- Prefer extraction when the helper represents a clear feature unit, policy, validation rule, protocol step, or reusable decision.
- Prefer extraction when a behavior change should naturally apply to every call site through one shared implementation.
- Flag over-extraction when it obscures local flow, hides important context, or creates names that merely restate the code.
- Flag under-extraction when repeated or conceptually distinct logic makes the caller harder to understand or risks inconsistent future changes.

## Security Review

- Prioritize issues that could let a client gain, request, or exercise privileges beyond what the server explicitly authorizes.
- Treat client-to-server capability negotiation as security-sensitive. Clients may request capabilities, but the server must make the final authorization decision with least privilege and default-deny behavior.
- Flag risky defaults where client SDKs request powerful capabilities by default, especially remote control, file transfer, admin operations, payment, session, group, authorization, or data export capabilities.
- Do not treat client-provided roles, scopes, groups, permissions, or capability declarations as authoritative without server-side verification.
- For Master, Gateway, and Dedicated changes, verify that the changed behavior preserves the intended trust boundary: Gateway is the external security boundary, Master is the control plane, and Dedicated owns authoritative game state.

## Test Coverage Review

- Check whether the PR reports relevant test results in the description, review discussion, CI, or validation notes.
- Treat missing focused tests as a merge-readiness blocker when changed behavior is practical to cover with tests, especially math utilities, parsers, serializers, protocol codecs, validators, pure business rules, permission decisions, state transformations, and regression-prone edge cases.
- Require the author to add tests and report the test result when testable behavior lacks coverage.
- Accept a no-new-test path only when the change is not practical to isolate, is mostly mechanical, or is better validated through integration/runtime checks; require that rationale and validation result to be stated.
- Verify that new test projects are placed under the solution's `Tests` solution folder/filter.

## Warning Policy

- Treat build, test, analyzer, nullable, compiler, package, and runtime warnings as merge-readiness blockers by default.
- Do not approve a pull request while warning-producing validation remains, even if the warning is outside the changed files, when the pull request reports or depends on that validation command.
- Accept warnings only when there is a clearly documented exceptional reason, the warning is not practical to eliminate in the pull request, and the remaining risk is understood.
- Require the author to either eliminate the warning or document the exceptional reason and provide clean validation for the commands that are expected to be warning-free.

## Dependency Review

- Check newly added or updated external libraries, including NuGet packages, for trustworthiness, maintenance, real-world adoption, and fit with existing repository conventions.
- For NuGet dependency changes, require vulnerability/advisory information to be checked before approval when that information is available. Prefer `dotnet list package --vulnerable --include-transitive` or equivalent package advisory evidence when practical.
- Treat known-vulnerable package versions as merge-readiness blockers unless the PR documents why the version is necessary, why safer versions are not practical, and what mitigation or follow-up exists.
- Do not block a package solely because no vulnerability data is available, but require stronger evidence that the package is established and maintained before accepting it.
- Do not assume Microsoft or framework-adjacent packages are automatically safe. Packages such as `Microsoft.AspNetCore.DataProtection` and `System.Security.Cryptography.Xml` still need advisory checks when added or updated.
- Ask the author to report the dependency trust and vulnerability-check result when a PR adds or updates external libraries without that evidence.

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
