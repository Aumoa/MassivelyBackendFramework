namespace OAuth2.DTO;

public readonly record struct AccountPictureUpdateResult(AccountPicture? Picture, AccountPictureError Error)
{
    public bool IsSuccess => Error == AccountPictureError.None && Picture != null;

    public static AccountPictureUpdateResult Success(AccountPicture picture)
    {
        return new AccountPictureUpdateResult(picture, AccountPictureError.None);
    }

    public static AccountPictureUpdateResult Failure(AccountPictureError error)
    {
        return new AccountPictureUpdateResult(null, error);
    }
}
