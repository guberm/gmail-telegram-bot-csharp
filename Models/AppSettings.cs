using Newtonsoft.Json;

namespace TelegramGmailBot.Models;

public class AppSettings
{
    [JsonProperty("telegram_bot_token")] public string TelegramBotToken { get; set; } = string.Empty;
    [JsonProperty("google_client_id")] public string GoogleClientId { get; set; } = string.Empty;
    [JsonProperty("google_client_secret")] public string GoogleClientSecret { get; set; } = string.Empty;
    [JsonProperty("polling_interval_seconds")] public int PollingIntervalSeconds { get; set; } = 60;
    [JsonProperty("database_path")] public string DatabasePath { get; set; } = "telegram_gmail.db";
    [JsonProperty("gmail_scopes")] public List<string> GmailScopes { get; set; } = new();
    [JsonProperty("oauth_callback_url")] public string OAuthCallbackUrl { get; set; } = "http://localhost:8080/oauth/callback";
    [JsonProperty("oauth_callback_port")] public int OAuthCallbackPort { get; set; } = 8080;
    [JsonProperty("application_name")] public string ApplicationName { get; set; } = "Telegram Gmail Bot";
    
    /// <summary>
    /// When true, the bot will send Telegram notifications after it synchronizes deletions
    /// from Gmail (e.g., "✅ Synced: X deleted emails removed from chat"). Default is false
    /// to avoid noisy messages. Configure in settings.json via "enable_sync_notifications".
    /// </summary>
    [JsonProperty("enable_sync_notifications")] public bool EnableSyncNotifications { get; set; } = false;
    
    public static AppSettings LoadFromFile(string path) =>
        JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
}