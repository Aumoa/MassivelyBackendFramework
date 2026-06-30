namespace OAuth2.DTO;

public record struct RawAccount
{
    public string Sub;
    public string Name;
    public string Email;
    public DateTime CreatedAt;
    public DateTime UpdatedAt;
}
