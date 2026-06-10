---
name: github-app-credential-policy
description: Choose credentials for GitHub commit-adjacent and online operations in MassivelyBackendFramework, especially push, branch publication, PR creation or updates, and remote validation.
---

# GitHub App Credential Policy

## Trigger

Use this skill before choosing credentials for GitHub operations, including push, remote branch publication, pull request creation or updates, PR comments, remote validation, or other online state changes.

Local `git commit` does not require GitHub credentials. Apply this policy when a commit is about to be pushed, associated with a pull request, or otherwise used in a remote GitHub workflow.

## Branch Classes

- Protected root branches include `dev`, `master`, `main`, release branches, production branches, and any branch or environment used by other users.
- Isolated work branches include `feature/*`, `codex/*`, `claude/*`, `copilot/*`, and other clearly task-specific branches created for separate feature development.

## Credential Selection

- For protected root branches, require ordinary user credentials. If the user provides ordinary user credentials and final approval, use those credentials for the operation.
- Do not use the GitHub App to push directly to protected root branches or to bypass branch protection, review, CI, release, or production safeguards.
- For isolated work branches, prefer the GitHub App when it is available and can satisfy the needed operation.
- If the GitHub App is unavailable for an isolated work branch, and the user provides ordinary user credentials, those credentials may be used.
- If neither the GitHub App nor user-provided ordinary credentials are available, do not perform the online operation. Continue with local validation and report the blocker.

## GitHub App Broker

- Prefer the local broker/client flow for GitHub App credentials:
  - Broker URL: `http://127.0.0.1:5657`
  - Client project: `CodexWorker.GitHubAuth.Client`
  - Secret file: `C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret`
- Request purpose-specific tokens instead of broad credentials. Use `push-codex-branch` for App-authenticated pushes to allowed work branches and `create-pr` for App-authenticated pull request creation.
- Pass the branch name when requesting a token for branch-scoped purposes, and expect non-allowed branches to fail closed.
- Never print, persist, commit, or place installation tokens in git remotes, repository files, shell history snippets, logs, or PR text.

## Approval Rules

- This credential policy does not replace the repository's Online Change Approval Policy.
- Protected shared targets still require final user approval before any online state change.
- Even on isolated work branches, avoid destructive remote operations or changes that can affect other users without explicit approval.
