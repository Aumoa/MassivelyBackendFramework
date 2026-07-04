namespace SecretGate.Models;

public sealed record OneTimeShareReceipt(
    string UrlToken,
    string AccessKey,
    DateTime ExpiresAtUtc);
