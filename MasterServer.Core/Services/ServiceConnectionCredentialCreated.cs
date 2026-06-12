namespace MasterServer.Services;

public sealed record ServiceConnectionCredentialCreated(
    ServiceConnectionCredentialInfo Credential,
    string SharedSecret);
