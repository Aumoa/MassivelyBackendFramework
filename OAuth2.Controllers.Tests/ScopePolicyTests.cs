using OAuth2;

namespace OAuth2.Controllers.Tests;

public sealed class ScopePolicyTests
{
    [Fact]
    public void SupportedScopes_IncludeOfflineAccess()
    {
        Assert.Contains(ScopePolicy.OfflineAccessScope, ScopePolicy.SupportedScopes);
    }

    [Fact]
    public void DefaultClientScopes_DoNotIncludeOfflineAccess()
    {
        Assert.DoesNotContain(ScopePolicy.OfflineAccessScope, ScopePolicy.DefaultClientScopes);
    }

    [Fact]
    public void ExpandAllForExternalClient_DoesNotImplicitlyRequestOfflineAccess()
    {
        var expanded = ScopePolicy.ExpandAllForExternalClient(ScopePolicy.AllScope);

        Assert.DoesNotContain(ScopePolicy.OfflineAccessScope, ScopePolicy.Split(expanded));
    }
}
