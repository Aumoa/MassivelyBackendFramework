# Codex Workflow Rules

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
