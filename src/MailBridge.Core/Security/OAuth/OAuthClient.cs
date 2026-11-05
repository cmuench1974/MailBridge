using System.Text;
using System.Text.Json;

namespace MailBridge.Core.Security.OAuth;

/// <summary>
/// Minimal OAuth2 client for a public desktop app: builds the authorization URL and
/// talks to the token endpoint (code redemption and refresh). Provider-neutral; the
/// provider specifics live in <see cref="OAuthProviderOptions"/>.
/// </summary>
public sealed class OAuthClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public OAuthClient(HttpClient http, TimeProvider? timeProvider = null)
    {
        _http = http;
        _time = timeProvider ?? TimeProvider.System;
    }

    public Uri BuildAuthorizationUri(OAuthProviderOptions options, string redirectUri, string state, string codeChallenge, string? loginHint = null)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("client_id", options.ClientId),
            new("response_type", "code"),
            new("redirect_uri", redirectUri),
            new("scope", string.Join(' ', options.Scopes)),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
        };

        if (options.ExtraAuthorizationParameters is { } extra)
        {
            parameters.AddRange(extra);
        }

        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            parameters.Add(new("login_hint", loginHint.Trim()));
        }

        var query = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return new Uri($"{options.AuthorizationEndpoint.AbsoluteUri}?{query}");
    }

    public Task<OAuthTokens> RedeemCodeAsync(OAuthProviderOptions options, string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken = default) =>
        RequestTokensAsync(options, new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = string.Join(' ', options.Scopes),
        }, cancellationToken);

    public Task<OAuthTokens> RefreshAsync(OAuthProviderOptions options, string refreshToken, CancellationToken cancellationToken = default) =>
        RequestTokensAsync(options, new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = string.Join(' ', options.Scopes),
        }, cancellationToken);

    private async Task<OAuthTokens> RequestTokensAsync(OAuthProviderOptions options, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(options.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new OAuthException("invalid_response", $"The token endpoint answered HTTP {(int)response.StatusCode} without a JSON body.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OAuthException("invalid_response", $"The token endpoint answered HTTP {(int)response.StatusCode} with an unexpected body.");
            }

            if (GetString(root, "error") is { } error)
            {
                throw new OAuthException(error, GetString(root, "error_description"));
            }

            if (!response.IsSuccessStatusCode || GetString(root, "access_token") is not { } accessToken)
            {
                throw new OAuthException("invalid_response", $"The token endpoint answered HTTP {(int)response.StatusCode} without an access token.");
            }

            var expiresIn = GetSeconds(root, "expires_in") ?? 3600;
            return new OAuthTokens(
                accessToken,
                GetString(root, "refresh_token"),
                _time.GetUtcNow().AddSeconds(expiresIn),
                ReadUsernameFromIdToken(GetString(root, "id_token")));
        }
    }

    /// <summary>
    /// Reads the account's email address from an id_token. The signature is not checked:
    /// the token comes straight from the token endpoint over TLS (OpenID Connect Core
    /// 3.1.3.7), and the value only pre-fills the IMAP username, which the server verifies.
    /// </summary>
    public static string? ReadUsernameFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

            // "email" first: for personal Microsoft accounts "preferred_username" can be a phone number.
            return new[] { "email", "preferred_username", "upn" }
                .Select(claim => GetString(document.RootElement, claim))
                .FirstOrDefault(value => value is not null && value.Contains('@'));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static int? GetSeconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
