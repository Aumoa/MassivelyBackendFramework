namespace OpenIDConnect;

public record OIDCOptions
{
    public required string Uri { get; set; }

    public required string ClientId { get; set; }

    public required string ClientSecret { get; set; }

    public string? CookiePrefix { get; set; }

    public bool AcceptLegacyCookieNames { get; set; }
}
