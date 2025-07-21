namespace Scripting.DTO;

public enum ResponseCode
{
    Success = 0,
    InvalidClientId = -1,
    DuplicateClientId = -2,
    ClientNotFound = -3
}
