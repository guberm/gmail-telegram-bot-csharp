namespace TelegramGmailBot.Models;

public class PaginationState
{
    public long ChatId { get; set; }
    public int CurrentPage { get; set; }
    public string? NextPageToken { get; set; }
    public string? PreviousPageToken { get; set; }
    public bool HasMore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}