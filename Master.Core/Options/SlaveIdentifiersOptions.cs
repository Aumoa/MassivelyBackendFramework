namespace Master.Options;

internal record SlaveIdentifiersOptions
{
    public string GatewayId { get; set; } = string.Empty;
    public string AuthId { get; set; } = string.Empty;
}
