using SecretGate.Services;

namespace SecretGate.Tests;

public sealed class SecretGateTokenGeneratorTests
{
    [Fact]
    public void CreateAccessKeyUsesExpectedFormat()
    {
        var generator = new SecretGateTokenGenerator();

        var accessKey = generator.CreateAccessKey();

        Assert.True(SecretGateTokenGenerator.IsAccessKeyFormat(accessKey));
        Assert.Equal('-', accessKey[4]);
    }

    [Fact]
    public void NormalizeAccessKeyTrimsAndUppercases()
    {
        var normalized = SecretGateTokenGenerator.NormalizeAccessKey(" abcd-2345 ");

        Assert.Equal("ABCD-2345", normalized);
    }
}
