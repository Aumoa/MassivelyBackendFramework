# Codex Workflow Rules

## Authentication Scope Policy

- Keep OpenID/OAuth scopes as narrow as the service can reasonably support.
- When a service uses `ProfileCard`, prefer including `profile` and `email` in the requested scopes because the component displays user name, profile image, and email.
- `profile` and `email` may still be omitted when there is a clear product, privacy, or technical reason, but make that tradeoff explicit.
- Do not request unrelated scopes such as `address` or `phone` unless the service actually uses the corresponding claims.

## Design Theme Policy

- Use OAuth2's current visual language as the default design baseline for services: calm surfaces, restrained borders, compact rounded corners, clear section hierarchy, and practical form/table layouts.
- Preserve a service's own product identity when it has a clear domain-specific design need, but otherwise align new UI work with the OAuth2 design baseline.
- Services with a user interface should respect the user's system color scheme by default.
- Prefer `prefers-color-scheme` or equivalent platform support to select light or dark theme automatically.
- Keep shared UI components theme-aware by using service-level design tokens instead of hard-coded light-only colors.
- If a service intentionally forces a single theme, make the product or technical reason explicit.

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
