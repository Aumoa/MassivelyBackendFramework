namespace Scripting.DTO;

public enum ResponseCode
{
    Success = 0,
    InvalidClientId = -1,
    DuplicateClientId = -2,
    ClientNotFound = -3,
    AccountAlreadyRegistered = -4,
    AccountOrPasswordInvalid = -5,
    AccountNotFound = -6,
    AccountEmailDuplicated = -7,

    Error_User = -65536,
}
