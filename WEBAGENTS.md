# Web Service Instructions

## Authentication Scope Policy

- Keep OpenID/OAuth scopes as narrow as the service can reasonably support.
- When a service uses `ProfileCard`, prefer including `profile` and `email` in the requested scopes because the component displays user name, profile image, and email.
- `profile` and `email` may still be omitted when there is a clear product, privacy, or technical reason, but make that tradeoff explicit.
- Do not request unrelated scopes such as `address` or `phone` unless the service actually uses the corresponding claims.

## Design Theme Policy

- Use OAuth2's current visual language as the default design baseline for services: calm surfaces, restrained borders, compact rounded corners, clear section hierarchy, and practical form/table layouts.
- Preserve a service's own product identity when it has a clear domain-specific design need, but otherwise align new UI work with the OAuth2 design baseline.
- Services with a user interface should respect the user's system color scheme by default.
- Prefer `prefers-color-scheme` or equivalent platform support to select light or dark theme automatically.
- Keep shared UI components theme-aware by using service-level design tokens instead of hard-coded light-only colors.
- If a service intentionally forces a single theme, make the product or technical reason explicit.
