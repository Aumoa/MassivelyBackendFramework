---
name: github-app-credential-policy
description: Choose credentials for GitHub commit-adjacent and online operations in MassivelyBackendFramework, especially push, branch publication, PR creation or updates, and remote validation.
---

# GitHub App Credential Policy

## Trigger

Use this skill before choosing credentials for GitHub operations, including push, remote branch publication, pull request creation or updates, author-side PR responses, remote validation, or other online state changes.

Local `git commit` does not require GitHub credentials. Apply this policy when a commit is about to be pushed, associated with a pull request, or otherwise used in a remote GitHub workflow.

## Branch Classes

- Protected root branches include `dev`, `master`, `main`, release branches, production branches, and any branch or environment used by other users.
- Isolated work branches include `feature/*`, `codex/*`, `claude/*`, `copilot/*`, and other clearly task-specific branches created for separate feature development.

## Credential Selection

- For PR comments and review-adjacent writes, follow `PR Comment Credential Rule` below before applying branch-class credential defaults.
- For protected root branches, require ordinary user credentials. If the user provides ordinary user credentials and final approval, use those credentials for the operation.
- Do not use the GitHub App to push directly to protected root branches or to bypass branch protection, review, CI, release, or production safeguards.
- For isolated work branches, prefer the GitHub App when it is available and can satisfy the needed operation.
- If the GitHub App is unavailable for an isolated work branch, and the user provides ordinary user credentials, those credentials may be used.
- If neither the GitHub App nor user-provided ordinary credentials are available, do not perform the online operation. Continue with local validation and report the blocker.

## PR Comment Credential Rule

- PR review feedback, approvals, change requests, and independent code evaluation must use the current ordinary GitHub user account.
- Do not use the GitHub App, bot identity, or installation token for review feedback that should appear as the current user's reviewer judgment. If suitable current-user credentials are unavailable, return the review findings to the user instead of posting them remotely.
- PR author or implementer responses must use the GitHub App or bot account. This includes replies to review feedback, pushed-commit explanations, PR description updates, validation-result updates, and user-requested re-review notes.
- If an author-side response cannot be posted with the bot account, do not fall back to the ordinary user account. Stop and report the failure, including the missing broker purpose, App permission, or connector capability that is needed.
- Do not assume the GitHub connector's integration credential is equivalent to the local `codex-worker-aumoa` GitHub App broker credential.
- For GitHub writes that must be bot-authored, prefer the broker token and verify the resulting `user.login` is the expected bot login, `codex-worker-aumoa[bot]`.
- If a write has a required actor and receives a 401/403 or permission error, do not retry with a different actor. Diagnose and report the credential or permission mismatch.

## GitHub App Broker

- Prefer the local broker/client flow for GitHub App credentials:
  - Broker URL: `http://127.0.0.1:5657`
  - Client project: `CodexWorker.GitHubAuth.Client`
  - Secret file: `C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret`
- Request purpose-specific tokens instead of broad credentials. Use `push-codex-branch` for App-authenticated pushes to allowed work branches, `create-pr` for App-authenticated pull request creation or updates, and `comment-pr` for bot-authored author-side PR comments or responses when configured.
- If `comment-pr` is missing or cannot satisfy a required bot-authored PR response, report the broker gap instead of silently using a non-bot actor.
- Pass the branch name when requesting a token for branch-scoped purposes, and expect non-allowed branches to fail closed.
- Never print, persist, commit, or place installation tokens in git remotes, repository files, shell history snippets, logs, or PR text.

## App Commit Identity

- For new commits on isolated work branches that will use the GitHub App for the related remote workflow, prefer the broker-provided App identity for the commit author and committer.
- Resolve identity with `CodexWorker.GitHubAuth.Client identity` and the broker secret file. The broker should be configured with an `AppIdentityCachePath`; use the cached identity first and let the broker call GitHub only when the cache is missing or invalid.
- The expected local cache path for `codex-worker-aumoa` is `C:\Users\liberty\.secrets\codex-worker-aumoa-app-identity.json`; Docker deployments should mount the same file and configure `/run/secrets/codex-worker-aumoa-app-identity.json`.
- Use the returned `gitUserName` and `gitUserEmail` with per-command git config, for example `git -c user.name=... -c user.email=... commit ...`.
- Do not rewrite existing commits solely to change author identity unless the user asks for that rewrite.
- If the broker identity endpoint is unavailable, keep the normal local git identity rather than inventing a bot email.

## Approval Rules

- This credential policy does not replace the repository's Online Change Approval Policy.
- Protected shared targets still require final user approval before any online state change.
- Even on isolated work branches, avoid destructive remote operations or changes that can affect other users without explicit approval.
