using Microsoft.AspNetCore.Mvc;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route(".well-known")]
public class WellKnownController(IJwt jwt) : ControllerBase
{
    [HttpGet("certs")]
    public IActionResult GetCerts()
    {
        return Ok(new
        {
            keys = (dynamic[])
            [
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = jwt.KId,
                    n = jwt.Modulus,
                    e = jwt.Exponent
                }
            ]
        });
    }

    [HttpGet("openid-configuration")]
    public IActionResult GetOpenIDConfiguration()
    {
        return Ok(new
        {
            issuer = jwt.Issuer,
            authorization_endpoint = jwt.Issuer + "/authorize",
            token_endpoint = jwt.Issuer + "/api/v1/token",
            userinfo_endpoint = jwt.Issuer + "/api/v1/userinfo",
            jwks_uri = jwt.Issuer + "/.well-known/certs",
            response_types_supported = (string[])["code"],
            response_modes_supported = (string[])["query"],
            subject_types_supported = (string[])["public"],
            id_token_signing_alg_values_supported = (string[])["RS256"],
            scopes_supported = ScopePolicy.SupportedScopes,
            acr_values_supported = OidcPolicy.SupportedAcrValues,
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post", "none" },
            claims_supported = (string[])
            [
                "acr",
                "address",
                "aud",
                "auth_time",
                "birthdate",
                "email",
                "email_verified",
                "exp",
                "family_name",
                "gender",
                "given_name",
                "iat",
                "iss",
                "locale",
                "middle_name",
                "name",
                "nbf",
                "nickname",
                "phone_number",
                "phone_number_verified",
                "picture",
                "preferred_username",
                "profile",
                "sub",
                "updated_at",
                "website",
                "zoneinfo"
            ],
            grant_types_supported = (string[])["authorization_code", "refresh_token"],
            code_challenge_methods_supported = (string[])["S256"],
            request_parameter_supported = false,
            request_uri_parameter_supported = false
        });
    }
}
