namespace OAuth2.Options;

public record EmailVerifyOptions
{
    public required string AccessKey { get; set; }

    public required string SecretKey { get; set; }

    public required string Region { get; set; }

    public required string SenderAddress { get; set; }
}
