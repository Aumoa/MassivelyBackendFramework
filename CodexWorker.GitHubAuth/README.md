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
    "PrivateKeyPath": "C:\\Users\\liberty\\.secrets\\codex-worker-aumoa.pem",
    "SharedSecretPath": "C:\\Users\\liberty\\.secrets\\codex-worker-aumoa-broker.secret",
    "AppIdentityCachePath": "C:\\Users\\liberty\\.secrets\\codex-worker-aumoa-app-identity.json",
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

Run the broker with:

```powershell
dotnet run --project CodexWorker.GitHubAuth -- --config C:\Users\liberty\.secrets\codex-worker-aumoa.json
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

When running in a Linux container, use container paths in the config file. Windows paths
such as `C:\Users\...` do not exist inside the container after mounting the secret
directory.

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
    "AppIdentityCachePath": "/run/secrets/codex-worker-aumoa-app-identity.json",
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
docker run --rm `
  --name codex-worker-github-auth `
  -p 127.0.0.1:5657:5657 `
  -v C:\Users\liberty\.secrets:/run/secrets:ro `
  codex-worker-github-auth
```

To confirm the mount shape:

```powershell
docker run --rm `
  --entrypoint /bin/sh `
  -v C:\Users\liberty\.secrets:/run/secrets:ro `
  codex-worker-github-auth `
  -c "ls -la /run/secrets"
```

## Client examples

Resolve the GitHub App bot identity without printing any token:

```powershell
dotnet run --project CodexWorker.GitHubAuth.Client -- identity --secret-file C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret
```

Use that identity for an App-authenticated work-branch commit:

```powershell
$identity = dotnet run --project CodexWorker.GitHubAuth.Client -- identity --secret-file C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret | ConvertFrom-Json
git -c user.name="$($identity.gitUserName)" -c user.email="$($identity.gitUserEmail)" commit -m "Codex: Example"
```

```powershell
dotnet run --project CodexWorker.GitHubAuth.Client -- token --repo Aumoa/MassivelyBackendFramework --purpose push-codex-branch --branch codex/example --secret-file C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret
```

```powershell
$env:GH_TOKEN = dotnet run --project CodexWorker.GitHubAuth.Client -- token --repo Aumoa/MassivelyBackendFramework --purpose create-pr --branch codex/example --secret-file C:\Users\liberty\.secrets\codex-worker-aumoa-broker.secret
gh pr create --head codex/example --base dev --title "Codex: Example" --body "Created by codex-worker-aumoa."
Remove-Item Env:\GH_TOKEN
```

For git HTTPS authentication, prefer an askpass wrapper that calls the client instead
of embedding the token in the remote URL.
