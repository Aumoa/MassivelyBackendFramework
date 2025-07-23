namespace Auth.Options;

internal record IdentifierOptions
{
    public string MasterUrl { get; set; } = string.Empty;
    public string SlaveId { get; set; } = string.Empty;
}
