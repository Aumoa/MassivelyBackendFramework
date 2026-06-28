---
name: github-app-credential-policy
description: Choose credentials for GitHub commit-adjacent and online operations in MassivelyBackendFramework, especially push, branch publication, PR creation or updates, and remote validation.
---

# GitHub App Credential Policy

## Trigger

Use this skill before choosing credentials for GitHub operations, including push, remote branch publication, pull request creation or updates, author-side PR responses, remote validation, or other online state changes.

Local `git commit` does not require a GitHub token by itself, but Codex-created commit identity on isolated work branches is still governed by this policy. Apply this policy before committing on a work branch unless the user explicitly says the commit is local-only or should use an ordinary user identity.

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
  - Secret file: resolve from the current user's home directory, for example `$env:USERPROFILE\.secrets\github-auth\codex-worker-aumoa-broker.secret` in PowerShell, `%USERPROFILE%\.secrets\github-auth\codex-worker-aumoa-broker.secret` in Windows `cmd`, or `~/.secrets/github-auth/codex-worker-aumoa-broker.secret` in Unix-like shells.
- Do not hard-code machine-specific absolute host paths in policy text, commands, scripts, or examples. Resolve secrets through the shell-appropriate home directory syntax, `CODEX_WORKER_GITHUB_AUTH_SECRET`, or an explicit user-provided `--secret-file`.
- Request purpose-specific tokens instead of broad credentials. Use `push-codex-branch` for App-authenticated pushes to allowed work branches, `create-pr` for App-authenticated pull request creation or updates, and `comment-pr` for bot-authored author-side PR comments or responses when configured.
- If `comment-pr` is missing or cannot satisfy a required bot-authored PR response, report the broker gap instead of silently using a non-bot actor.
- Pass the branch name when requesting a token for branch-scoped purposes, and expect non-allowed branches to fail closed.
- Never print, persist, commit, or place installation tokens in git remotes, repository files, shell history snippets, logs, or PR text.

## App Commit Identity

- For any new Codex-created commit on an isolated work branch, assume it will be pushed, associated with a pull request, or otherwise used in a GitHub App-authenticated or bot-authored remote workflow. Require the broker-provided App identity for the commit author and committer by default.
- If the user explicitly directs using an ordinary user account for a specific commit or workflow, that user instruction overrides the default App identity requirement for that scope.
- Resolve identity with `CodexWorker.GitHubAuth.Client identity` and the broker secret file. The broker should be configured with an `AppIdentityCachePath`; use the cached identity first and let the broker call GitHub only when the cache is missing or invalid.
- Store the `codex-worker-aumoa` identity cache under the user-relative GitHub Auth secret/cache directory, using the same shell-appropriate home directory syntax as the broker secret path. Docker deployments should mount the user-relative directory into the container and configure the container path consistently.
- Use the returned `gitUserName` and `gitUserEmail` with per-command git config, for example `git -c user.name=... -c user.email=... commit ...`.
- Do not rewrite existing commits solely to change author identity unless the user asks for that rewrite.
- If a just-created, unpublished Codex commit on an isolated work branch used the ordinary local identity and the user asks for bot identity, amend or otherwise rewrite that Codex commit with the broker-provided App identity before adding unrelated follow-up work.
- If the broker identity endpoint is unavailable, stop before creating the commit and report the blocker unless the user explicitly authorizes using the normal local git identity for that commit.
- Do not silently fall back to an ordinary user identity, and do not invent a bot email.

## Approval Rules

- This credential policy does not replace the repository's Online Change Approval Policy.
- Protected shared targets still require final user approval before any online state change.
- Even on isolated work branches, avoid destructive remote operations or changes that can affect other users without explicit approval.
