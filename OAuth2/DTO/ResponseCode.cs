namespace OAuth2.DTO;

public enum ResponseCode
{
    Success = 0,

    AccountEmailDuplicated = -1,
    AccountAlreadyRegistered = -2,
    AccountNotFound = -3
}
