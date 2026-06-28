using OAuth2;

namespace OAuth2.Controllers.Tests;

public sealed class ClientIdPolicyTests
{
    [Theory]
    [InlineData("my-app")]
    [InlineData("Game.Launcher_01")]
    [InlineData("client123")]
    public void TryNormalize_AcceptsReadableClientIds(string clientId)
    {
        var result = ClientIdPolicy.TryNormalize(clientId, out var normalizedClientId, out var error);

        Assert.True(result);
        Assert.Equal(clientId, normalizedClientId);
        Assert.Equal(ClientIdValidationError.None, error);
    }

    [Fact]
    public void TryNormalize_TrimsOuterWhitespace()
    {
        var result = ClientIdPolicy.TryNormalize("  my-app  ", out var normalizedClientId, out var error);

        Assert.True(result);
        Assert.Equal("my-app", normalizedClientId);
        Assert.Equal(ClientIdValidationError.None, error);
    }

    [Theory]
    [InlineData(null, ClientIdValidationError.Required)]
    [InlineData("", ClientIdValidationError.Required)]
    [InlineData("my app", ClientIdValidationError.InvalidCharacter)]
    [InlineData("my/app", ClientIdValidationError.InvalidCharacter)]
    public void TryNormalize_RejectsInvalidClientIds(string? clientId, ClientIdValidationError expectedError)
    {
        var result = ClientIdPolicy.TryNormalize(clientId, out var normalizedClientId, out var error);

        Assert.False(result);
        Assert.Equal(string.IsNullOrWhiteSpace(clientId) ? string.Empty : clientId.Trim(), normalizedClientId);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void TryNormalize_RejectsClientIdsLongerThanStorageLimit()
    {
        var clientId = new string('a', ClientIdPolicy.MaxLength + 1);

        var result = ClientIdPolicy.TryNormalize(clientId, out var normalizedClientId, out var error);

        Assert.False(result);
        Assert.Equal(clientId, normalizedClientId);
        Assert.Equal(ClientIdValidationError.TooLong, error);
    }
}
