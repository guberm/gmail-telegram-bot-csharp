namespace TelegramGmailBot.Models;

public class UserCredentials
{
    public long ChatId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
}
