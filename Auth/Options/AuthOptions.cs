namespace Auth.Options;

internal record AuthOptions
{
    public string RedirectUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
