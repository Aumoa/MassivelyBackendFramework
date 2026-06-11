namespace MasterAdmin.Services;

public sealed record ServiceConnectionCredentialCreated(
    ServiceConnectionCredentialInfo Credential,
    string SharedSecret);
