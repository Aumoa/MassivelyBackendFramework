namespace SecretGate.Services;

public sealed class SecretVaultSession
{
    private string? m_VaultKey;

    public bool IsUnlocked => !string.IsNullOrEmpty(m_VaultKey);

    public void Unlock(string vaultKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKey);
        m_VaultKey = vaultKey;
    }

    public void Lock()
    {
        m_VaultKey = null;
    }

    public string GetVaultKeyOrThrow()
    {
        return m_VaultKey
            ?? throw new InvalidOperationException("The vault is locked.");
    }
}
