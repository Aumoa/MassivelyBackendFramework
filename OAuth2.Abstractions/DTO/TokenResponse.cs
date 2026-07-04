using System.Text.Json.Serialization;

namespace OAuth2.DTO;

public record TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public required string Scope { get; set; }

    [JsonPropertyName("refresh_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RefreshExpiresIn { get; set; }

    [JsonPropertyName("id_token")]
    public required string? IdToken { get; set; }
}
