# CodexWorker.GitHubAuth

Local GitHub App installation-token broker for Codex worker tasks.

The server keeps the GitHub App private key outside the repository and issues one-hour
installation access tokens only for configured repository/purpose pairs. The client is a
small command-line helper that asks the broker for a token without storing it in the git
remote or repository files.

## Server configuration

Keep the real configuration outside this repository, for example:

```json
{
  "CodexWorkerGitHubAuth": {
    "ListenUrl": "http://127.0.0.1:5657",
    "AppId": "123456",
    "InstallationId": 12345678,
    "PrivateKeyPath": "<user-secret-dir>\\codex-worker-aumoa.pem",
    "SharedSecretPath": "<user-secret-dir>\\codex-worker-aumoa-broker.secret",
    "AppIdentityCachePath": "<user-cache-dir>\\codex-worker-aumoa-app-identity.json",
    "AllowedRepositories": [
      {
        "Name": "Aumoa/MassivelyBackendFramework",
        "Purposes": {
          "push-codex-branch": {
            "RequireBranch": true,
            "BranchPrefixes": [ "codex/" ],
            "Permissions": {
              "contents": "write"
            }
          },
          "create-pr": {
            "RequireBranch": true,
            "BranchPrefixes": [ "codex/" ],
            "Permissions": {
              "pull_requests": "write"
            }
          },
          "comment-pr": {
            "Permissions": {
              "issues": "write"
            }
          }
        }
      }
    ]
  }
}
```

Resolve `<user-secret-dir>` and `<user-cache-dir>` from a user-relative location such
as `$env:USERPROFILE\.secrets\github-auth` before writing the configuration file. Do
not put machine-specific absolute host paths in committed examples or shared instructions.

Run the broker with:

```powershell
$secretRoot = Join-Path $env:USERPROFILE ".secrets\github-auth"
dotnet run --project CodexWorker.GitHubAuth -- --config (Join-Path $secretRoot "codex-worker-github-auth.json")
```

The broker rejects non-loopback requests and browser `Origin` requests. It also requires
the shared secret through `Authorization: Bearer ...` or `X-Codex-Worker-Secret`.

### App identity cache

Set `AppIdentityCachePath` to avoid repeated GitHub API calls when resolving the bot
commit identity. The broker reads this JSON file first and only calls GitHub when the
file is missing or invalid. If GitHub lookup succeeds and the path is writable, the
broker writes the cache file automatically.

Example cache file:

```json
{
  "appSlug": "codex-worker-aumoa",
  "appName": "codex-worker-aumoa",
  "botLogin": "codex-worker-aumoa[bot]",
  "botUserId": 292147838,
  "gitUserName": "codex-worker-aumoa[bot]",
  "gitUserEmail": "292147838+codex-worker-aumoa[bot]@users.noreply.github.com"
}
```

## Docker configuration

When running in a Linux container, use container paths in the config file. Host
user-profile paths do not exist inside the container after mounting the secret directory.

```json
{
  "CodexWorkerGitHubAuth": {
    "ListenUrl": "http://0.0.0.0:5657",
    "AllowNonLoopbackListenUrl": true,
    "AllowNonLoopbackClientAddress": true,
    "AppId": "123456",
    "InstallationId": 12345678,
    "PrivateKeyPath": "/run/secrets/codex-worker-aumoa.pem",
    "SharedSecretPath": "/run/secrets/codex-worker-aumoa-broker.secret",
    "AppIdentityCachePath": "/run/cache/codex-worker-aumoa-app-identity.json",
    "AllowedRepositories": [
      {
        "Name": "Aumoa/MassivelyBackendFramework",
        "Purposes": {
          "push-codex-branch": {
            "RequireBranch": true,
            "BranchPrefixes": [ "codex/" ],
            "Permissions": {
              "contents": "write"
            }
          },
          "create-pr": {
            "RequireBranch": true,
            "BranchPrefixes": [ "codex/" ],
            "Permissions": {
              "pull_requests": "write"
            }
          }
        }
      }
    ]
  }
}
```

Only use `AllowNonLoopbackListenUrl` and `AllowNonLoopbackClientAddress` with a Docker
publish binding that exposes the broker on host loopback only:

```powershell
$secretRoot = Join-Path $env:USERPROFILE ".secrets\github-auth"
docker run --rm `
  --name codex-worker-github-auth `
  -p 127.0.0.1:5657:5657 `
  -v "${secretRoot}:/run/secrets:ro" `
  codex-worker-github-auth
```

To confirm the mount shape:

```powershell
$secretRoot = Join-Path $env:USERPROFILE ".secrets\github-auth"
docker run --rm `
  --entrypoint /bin/sh `
  -v "${secretRoot}:/run/secrets:ro" `
  codex-worker-github-auth `
  -c "ls -la /run/secrets"
```

## Client examples

Resolve the GitHub App bot identity without printing any token:

```powershell
$brokerSecret = Join-Path $env:USERPROFILE ".secrets\github-auth\codex-worker-aumoa-broker.secret"
dotnet run --project CodexWorker.GitHubAuth.Client -- identity --secret-file $brokerSecret
```

Use that identity for an App-authenticated work-branch commit:

```powershell
$brokerSecret = Join-Path $env:USERPROFILE ".secrets\github-auth\codex-worker-aumoa-broker.secret"
$identity = dotnet run --project CodexWorker.GitHubAuth.Client -- identity --secret-file $brokerSecret | ConvertFrom-Json
git -c user.name="$($identity.gitUserName)" -c user.email="$($identity.gitUserEmail)" commit -m "Codex: Example"
```

```powershell
$brokerSecret = Join-Path $env:USERPROFILE ".secrets\github-auth\codex-worker-aumoa-broker.secret"
dotnet run --project CodexWorker.GitHubAuth.Client -- token --repo Aumoa/MassivelyBackendFramework --purpose push-codex-branch --branch codex/example --secret-file $brokerSecret
```

```powershell
$brokerSecret = Join-Path $env:USERPROFILE ".secrets\github-auth\codex-worker-aumoa-broker.secret"
$env:GH_TOKEN = dotnet run --project CodexWorker.GitHubAuth.Client -- token --repo Aumoa/MassivelyBackendFramework --purpose create-pr --branch codex/example --secret-file $brokerSecret
gh pr create --head codex/example --base dev --title "Codex: Example" --body "Created by codex-worker-aumoa."
Remove-Item Env:\GH_TOKEN
```

For git HTTPS authentication, prefer an askpass wrapper that calls the client instead
of embedding the token in the remote URL.
