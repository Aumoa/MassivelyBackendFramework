using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class MySqlSecretRepositoryTests
{
    [Fact]
    public void ReadGuidAcceptsGuidValue()
    {
        var id = Guid.NewGuid();

        var result = MySqlSecretRepository.ReadGuid(id);

        Assert.Equal(id, result);
    }

    [Fact]
    public void ReadGuidAcceptsStringValue()
    {
        var id = Guid.NewGuid();

        var result = MySqlSecretRepository.ReadGuid(id.ToString("D"));

        Assert.Equal(id, result);
    }
}
