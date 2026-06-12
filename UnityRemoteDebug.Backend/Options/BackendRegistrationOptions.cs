namespace UnityRemoteDebug.Backend.Options;

public sealed record BackendRegistrationOptions
{
    public string BackendKind { get; set; } = "UnityRemoteDebug";
}
