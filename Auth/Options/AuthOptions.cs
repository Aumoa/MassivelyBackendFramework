namespace Auth.Options;

internal record AuthOptions
{
    public required string RedirectUrl { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
}
