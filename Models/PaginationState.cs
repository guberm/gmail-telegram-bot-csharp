namespace TelegramGmailBot.Models;

/// <summary>
/// Represents the pagination state for a user's email listing session.     
/// </summary>
public class PaginationState
{
    /// <summary>
    /// The chat ID of the user.
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// The current page number in the email listing.
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// The token for the next page of results, if available.
    /// </summary>
    public string? NextPageToken { get; set; }

    /// <summary>
    /// The token for the previous page of results, if available.
    /// </summary>
    public string? PreviousPageToken { get; set; }

    /// <summary>
    /// Indicates whether there are more pages of results available.
    /// </summary>
    public bool HasMore { get; set; }
    /// <summary>
    /// The timestamp when the pagination state was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The timestamp when the pagination state was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}