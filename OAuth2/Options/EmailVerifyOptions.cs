namespace OAuth2.Options;

public record EmailVerifyOptions
{
    public required string UserId { get; set; }

    public required string Secret { get; set; }

    public required string Sender { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; } = 587;
}
