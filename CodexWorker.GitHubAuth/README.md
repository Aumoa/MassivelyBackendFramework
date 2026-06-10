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

## Client examples

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
