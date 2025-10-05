using Newtonsoft.Json;

namespace TelegramGmailBot.Models;

public class AppSettings
{
    [JsonProperty("gmail_client_id")] public string GmailClientId { get; set; } = string.Empty;
    [JsonProperty("gmail_client_secret")] public string GmailClientSecret { get; set; } = string.Empty;
    [JsonProperty("telegram_bot_token")] public string TelegramBotToken { get; set; } = string.Empty;
    [JsonProperty("polling_interval_seconds")] public int PollingIntervalSeconds { get; set; } = 60;
    [JsonProperty("database_path")] public string DatabasePath { get; set; } = "telegram_gmail.db";
    [JsonProperty("gmail_scopes")] public List<string> GmailScopes { get; set; } = new();
    [JsonProperty("redirect_uri")] public string RedirectUri { get; set; } = "http://localhost:8080/";
    [JsonProperty("application_name")] public string ApplicationName { get; set; } = "Telegram Gmail Bot";
    public static AppSettings LoadFromFile(string path) =>
        JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
}
