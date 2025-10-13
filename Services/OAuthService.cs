using System.Security.Cryptography;
using Newtonsoft.Json;
using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

/// <summary>
/// Service for handling OAuth 2.0 authentication with Google for Gmail access.
/// </summary>
public class OAuthService
{
    private readonly DatabaseService _databaseService;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private const string GoogleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Initializes a new instance of the OAuthService.
    /// </summary>
    /// <param name="databaseService">The database service for storing OAuth states and user credentials.</param>
    /// <param name="settings">The application settings containing OAuth configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if databaseService or settings is null.</exception>
    public OAuthService(DatabaseService databaseService, AppSettings settings)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Generates the Google OAuth authorization URL for a user to initiate the OAuth flow.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <param name="clientId">The Google OAuth client ID.</param>
    /// <returns>The authorization URL.</returns>
    public string GenerateAuthorizationUrl(long chatId, string clientId)
    {
        var state = GenerateRandomState();
        var oauthState = new OAuthState
        {
            State = state,
            ChatId = chatId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        _databaseService.SaveOAuthState(oauthState);
        _databaseService.CleanupExpiredOAuthStates();

        var scopes = string.Join(" ", _settings.GmailScopes);
        var url = $"{GoogleAuthUrl}?" +
                  $"client_id={Uri.EscapeDataString(clientId)}&" +
                  $"redirect_uri={Uri.EscapeDataString(_settings.OAuthCallbackUrl)}&" +
                  $"response_type=code&" +
                  $"scope={Uri.EscapeDataString(scopes)}&" +
                  $"state={Uri.EscapeDataString(state)}&" +
                  $"access_type=offline&" +
                  $"prompt=consent";

        return url;
    }

    /// <summary>
    /// Exchanges an authorization code for user credentials.
    /// </summary>
    /// <param name="code">The authorization code received from Google.</param>
    /// <param name="clientId">The Google OAuth client ID.</param>
    /// <param name="clientSecret">The Google OAuth client secret.</param>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <returns>The user credentials if successful, otherwise null.</returns>
    /// <exception cref="ArgumentNullException">Thrown if code, clientId, or clientSecret is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the OAuth state is invalid or expired.</exception>
    /// <remarks>
    /// This method exchanges the authorization code for access and refresh tokens,
    /// and retrieves the user's email address.
    /// </remarks>
    public async Task<UserCredentials?> ExchangeCodeForTokensAsync(string code, string clientId, string clientSecret, long chatId)
    {
        try
        {
            var requestData = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "redirect_uri", _settings.OAuthCallbackUrl },
                { "grant_type", "authorization_code" }
            };

            var response = await _httpClient.PostAsync(GoogleTokenUrl, new FormUrlEncodedContent(requestData));
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Token exchange failed: {responseContent}");
                return null;
            }

            var tokenResponse = JsonConvert.DeserializeObject<GoogleTokenResponse>(responseContent);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                return null;

            var credentials = new UserCredentials
            {
                ChatId = chatId,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            credentials.EmailAddress = await GetUserEmailAsync(credentials.AccessToken) ?? string.Empty;
            Console.WriteLine($"[DEBUG] Retrieved user email: '{credentials.EmailAddress}'");
            _databaseService.SaveUserCredentials(credentials);
            Console.WriteLine($"[DEBUG] User credentials saved to database");
            return credentials;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exchanging code for tokens: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Refreshes the user's access token using their refresh token.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <param name="clientId">The Google OAuth client ID.</param>
    /// <param name="clientSecret">The Google OAuth client secret.</param>
    /// <returns>True if the token was successfully refreshed, otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if clientId or clientSecret is null or empty.</exception>
    /// <remarks>
    /// This method retrieves the user's current credentials from the database,
    /// refreshes the access token using the refresh token, and updates the database.
    /// </remarks>
    public async Task<bool> RefreshAccessTokenAsync(long chatId, string clientId, string clientSecret)
    {
        try
        {
            var credentials = _databaseService.GetUserCredentials(chatId);
            if (credentials == null || string.IsNullOrEmpty(credentials.RefreshToken))
                return false;

            var requestData = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "refresh_token", credentials.RefreshToken },
                { "grant_type", "refresh_token" }
            };

            var response = await _httpClient.PostAsync(GoogleTokenUrl, new FormUrlEncodedContent(requestData));
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Token refresh failed: {responseContent}");
                return false;
            }

            var tokenResponse = JsonConvert.DeserializeObject<GoogleTokenResponse>(responseContent);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                return false;

            credentials.AccessToken = tokenResponse.AccessToken;
            credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            credentials.UpdatedAt = DateTime.UtcNow;

            _databaseService.SaveUserCredentials(credentials);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error refreshing token: {ex.Message}");
            return false;
        }
    }

    private async Task<string?> GetUserEmailAsync(string accessToken)
    {
        Console.WriteLine($"[DEBUG] GetUserEmailAsync called with token length: {accessToken?.Length ?? 0}");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            Console.WriteLine("[DEBUG] Sending request to Google userinfo API...");
            
            var response = await _httpClient.SendAsync(request);
            Console.WriteLine($"[DEBUG] Userinfo API response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[DEBUG] Userinfo API response content: {content}");
                var userInfo = JsonConvert.DeserializeObject<GoogleUserInfo>(content);
                Console.WriteLine($"[DEBUG] Parsed email: '{userInfo?.Email}'");
                return userInfo?.Email;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[ERROR] Userinfo API failed: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in GetUserEmailAsync: {ex.Message}");
        }
        return null;
    }

    private string GenerateRandomState()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private class GoogleTokenResponse
    {
        [JsonProperty("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonProperty("refresh_token")] public string? RefreshToken { get; set; }
        [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
        [JsonProperty("token_type")] public string TokenType { get; set; } = string.Empty;
    }

    private class GoogleUserInfo
    {
        [JsonProperty("email")] public string Email { get; set; } = string.Empty;
    }
}
