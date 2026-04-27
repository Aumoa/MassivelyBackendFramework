namespace OAuth2.Options;

public record RegisterOptions
{
    public string[] AllowedEmailDomains { get; set; } = [];
}
