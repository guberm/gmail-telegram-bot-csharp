using Newtonsoft.Json;

namespace TelegramGmailBot.Models;

/// <summary>
/// Configuration settings for the Telegram Gmail Bot application, loaded from JSON configuration files.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// The authentication token for the Telegram bot.
    /// </summary>
    [JsonProperty("telegram_bot_token")] public string TelegramBotToken { get; set; } = string.Empty;
    
    /// <summary>
    /// The Google OAuth client ID for Gmail API access.
    /// </summary>
    [JsonProperty("google_client_id")] public string GoogleClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// The Google OAuth client secret for Gmail API access.
    /// </summary>
    [JsonProperty("google_client_secret")] public string GoogleClientSecret { get; set; } = string.Empty;
    
    /// <summary>
    /// The interval in seconds between email polling cycles. Default is 60 seconds.
    /// </summary>
    [JsonProperty("polling_interval_seconds")] public int PollingIntervalSeconds { get; set; } = 60;
    
    /// <summary>
    /// The file path for the SQLite database. Default is "telegram_gmail.db".
    /// </summary>
    [JsonProperty("database_path")] public string DatabasePath { get; set; } = "telegram_gmail.db";
    
    /// <summary>
    /// The list of Gmail API scopes required for the application.
    /// </summary>
    [JsonProperty("gmail_scopes")] public List<string> GmailScopes { get; set; } = new();
    
    /// <summary>
    /// The callback URL for OAuth authentication. Default is "http://localhost:8080/oauth/callback".
    /// </summary>
    [JsonProperty("oauth_callback_url")] public string OAuthCallbackUrl { get; set; } = "http://localhost:8080/oauth/callback";
    
    /// <summary>
    /// The port number for the OAuth callback server. Default is 8080.
    /// </summary>
    [JsonProperty("oauth_callback_port")] public int OAuthCallbackPort { get; set; } = 8080;
    
    /// <summary>
    /// The name of the application. Default is "Telegram Gmail Bot".
    /// </summary>
    [JsonProperty("application_name")] public string ApplicationName { get; set; } = "Telegram Gmail Bot";
    
    /// <summary>
    /// When true, the bot will send Telegram notifications after it synchronizes deletions
    /// from Gmail (e.g., "✅ Synced: X deleted emails removed from chat"). Default is false
    /// to avoid noisy messages. Configure in settings.json via "enable_sync_notifications".
    /// </summary>
    [JsonProperty("enable_sync_notifications")] public bool EnableSyncNotifications { get; set; } = false;
    
    /// <summary>
    /// Loads application settings from a JSON configuration file.
    /// </summary>
    /// <param name="path">The path to the JSON configuration file.</param>
    /// <returns>The loaded AppSettings instance, or a new instance with default values if loading fails.</returns>
    public static AppSettings LoadFromFile(string path) =>
        JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
}