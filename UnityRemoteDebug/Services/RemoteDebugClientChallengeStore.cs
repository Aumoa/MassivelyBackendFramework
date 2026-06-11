using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugClientChallengeStore
{
    private const int ChallengeIdBytes = 24;
    private const int NonceBytes = 32;
    private readonly ConcurrentDictionary<string, RemoteDebugClientChallenge> m_Challenges = [];

    internal RemoteDebugClientChallenge Create(TimeSpan lifetime)
    {
        CleanupExpired();

        var challenge = new RemoteDebugClientChallenge(
            Base64UrlEncode(RandomNumberGenerator.GetBytes(ChallengeIdBytes)),
            RandomNumberGenerator.GetBytes(NonceBytes),
            DateTimeOffset.UtcNow.Add(lifetime));

        m_Challenges[challenge.ChallengeId] = challenge;
        return challenge;
    }

    internal bool TryConsume(string challengeId, out RemoteDebugClientChallenge challenge)
    {
        challenge = null!;
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            return false;
        }

        if (!m_Challenges.TryRemove(challengeId, out var candidate))
        {
            return false;
        }

        if (candidate.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        challenge = candidate;
        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var challenge in m_Challenges)
        {
            if (challenge.Value.ExpiresAt < now)
            {
                m_Challenges.TryRemove(challenge.Key, out _);
            }
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed record RemoteDebugClientChallenge(
    string ChallengeId,
    byte[] Nonce,
    DateTimeOffset ExpiresAt);
