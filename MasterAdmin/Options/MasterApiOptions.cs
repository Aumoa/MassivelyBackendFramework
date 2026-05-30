namespace MasterAdmin.Options;

public sealed record MasterApiOptions
{
    public string BaseAddress { get; set; } = "http://localhost:5265";

    public int TimeoutMilliseconds { get; set; } = 5000;
}
