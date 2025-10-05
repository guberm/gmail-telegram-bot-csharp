namespace TelegramGmailBot.Models;

public class OAuthState
{
    public string State { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
