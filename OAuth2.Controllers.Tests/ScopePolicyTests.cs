using OAuth2;

namespace OAuth2.Controllers.Tests;

public sealed class ScopePolicyTests
{
    [Fact]
    public void DefaultClientScopes_IncludeOfflineAccess()
    {
        Assert.Contains(ScopePolicy.OfflineAccessScope, ScopePolicy.DefaultClientScopes);
    }

    [Fact]
    public void ExpandAllForExternalClient_DoesNotImplicitlyRequestOfflineAccess()
    {
        var expanded = ScopePolicy.ExpandAllForExternalClient(ScopePolicy.AllScope);

        Assert.DoesNotContain(ScopePolicy.OfflineAccessScope, ScopePolicy.Split(expanded));
    }
}
