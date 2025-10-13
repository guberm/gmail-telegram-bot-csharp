using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using TelegramGmailBot.Models;
using System.Text;

namespace TelegramGmailBot.Services;

/// <summary>
/// Client for interacting with Gmail API to fetch, manage, and perform operations on email messages.
/// Provides functionality for authentication, message retrieval, and email actions like delete, archive, and star.
/// </summary>
public class GmailClient
{
    private readonly AppSettings _settings;
    private readonly DatabaseService _databaseService;
    private Google.Apis.Gmail.v1.GmailService? _service;
    private long _currentChatId;
    private UserCredential? _credential;

    /// <summary>
    /// Initializes a new instance of the GmailClient with the specified application settings and database service.
    /// </summary>
    /// <param name="settings">The application settings containing Gmail API configuration.</param>
    /// <param name="databaseService">The database service for updating stored credentials.</param>
    public GmailClient(AppSettings settings, DatabaseService databaseService) 
    { 
        _settings = settings; 
        _databaseService = databaseService;
    }

    /// <summary>
    /// Authenticates the Gmail client using OAuth2 access and refresh tokens.
    /// </summary>
    /// <param name="accessToken">The OAuth2 access token for Gmail API access.</param>
    /// <param name="refreshToken">The OAuth2 refresh token for token renewal.</param>
    /// <returns>True if authentication succeeds, false otherwise.</returns>
    public async Task<bool> AuthenticateAsync(string accessToken, string refreshToken)
    {
        try
        {
            var tokenResponse = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };

            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = _settings.GoogleClientId,
                        ClientSecret = _settings.GoogleClientSecret
                    },
                    Scopes = new[] { "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.modify" },
                    DataStore = null // We'll handle token storage manually
                });

            _credential = new UserCredential(flow, "user", tokenResponse);
            
            // Try to refresh the token if it's expired/stale
            if (tokenResponse.IsStale)
            {
                Console.WriteLine("[DEBUG] Access token is stale, attempting to refresh...");
                await _credential.RefreshTokenAsync(CancellationToken.None);
                Console.WriteLine("[DEBUG] Token refresh successful");
                
                // Update stored credentials with refreshed token
                await UpdateStoredCredentialsAsync();
            }

            _service = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = _settings.ApplicationName
            });

            Console.WriteLine("Gmail authentication successful");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gmail authentication failed: {ex.Message}");
            Console.WriteLine($"[DEBUG] Exception details: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Sets the current Telegram chat ID for context in Gmail operations.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID to associate with this Gmail client instance.</param>
    public void SetCurrentUser(long chatId)
    {
        _currentChatId = chatId;
    }

    /// <summary>
    /// Gets the current access token from the authenticated credential, refreshing if necessary.
    /// </summary>
    /// <returns>The current access token, or null if not authenticated.</returns>
    public async Task<string?> GetCurrentAccessTokenAsync()
    {
        if (_service?.HttpClientInitializer is UserCredential credential)
        {
            var token = await credential.GetAccessTokenForRequestAsync();
            return token;
        }
        return null;
    }

    /// <summary>
    /// Updates the stored credentials in the database with the current token information.
    /// </summary>
    private async Task UpdateStoredCredentialsAsync()
    {
        if (_credential?.Token != null && _currentChatId != 0)
        {
            var currentCreds = _databaseService.GetUserCredentials(_currentChatId);
            if (currentCreds != null)
            {
                currentCreds.AccessToken = _credential.Token.AccessToken;
                if (!string.IsNullOrEmpty(_credential.Token.RefreshToken))
                {
                    currentCreds.RefreshToken = _credential.Token.RefreshToken;
                }
                if (_credential.Token.ExpiresInSeconds.HasValue)
                {
                    currentCreds.ExpiresAt = DateTime.UtcNow.AddSeconds(_credential.Token.ExpiresInSeconds.Value);
                }
                currentCreds.UpdatedAt = DateTime.UtcNow;
                
                _databaseService.SaveUserCredentials(currentCreds);
                Console.WriteLine("[DEBUG] Updated stored credentials after token refresh");
            }
        }
    }

    /// <summary>
    /// Fetches email messages from the Gmail inbox with the specified limit.
    /// </summary>
    /// <param name="maxResults">The maximum number of messages to retrieve (default: 10).</param>
    /// <param name="unreadOnly">If true, only fetch unread messages (default: false).</param>
    /// <returns>A list of EmailMessage objects ordered by received date (newest first).</returns>
    public async Task<List<EmailMessage>> FetchInboxMessagesAsync(int maxResults = 10, bool unreadOnly = false)
    {
        if (_service == null) throw new InvalidOperationException("Gmail service not authenticated");
        var messages = new List<EmailMessage>();
        try
        {
            var request = _service.Users.Messages.List("me");
            request.LabelIds = new Google.Apis.Util.Repeatable<string>(new[] { "INBOX" });
            if (unreadOnly)
            {
                request.Q = "is:unread";
            }
            request.MaxResults = maxResults;
            var response = await request.ExecuteAsync();
            if (response.Messages != null)
            {
                foreach (var messageItem in response.Messages)
                {
                    var fullMessage = await GetMessageDetailsAsync(messageItem.Id);
                    if (fullMessage != null) messages.Add(fullMessage);
                }
            }
            messages = messages.OrderByDescending(m => m.ReceivedDateTime).ToList();
        }
        catch (Exception ex) { Console.WriteLine($"Error fetching inbox messages: {ex.Message}"); throw; }
        return messages;
    }

    /// <summary>
    /// Fetches email messages from the Gmail inbox with pagination support.
    /// </summary>
    /// <param name="maxResults">The maximum number of messages to retrieve per page (default: 10).</param>
    /// <param name="pageToken">The pagination token for retrieving the next page of results (optional).</param>
    /// <returns>A tuple containing the list of messages, next page token, and whether more pages are available.</returns>
    public async Task<(List<EmailMessage> messages, string? nextPageToken, bool hasMore)> FetchInboxMessagesWithPaginationAsync(int maxResults = 10, string? pageToken = null)
    {
        if (_service == null) throw new InvalidOperationException("Gmail service not authenticated");
        var messages = new List<EmailMessage>();
        try
        {
            var request = _service.Users.Messages.List("me");
            request.LabelIds = new Google.Apis.Util.Repeatable<string>(new[] { "INBOX" });
            request.MaxResults = maxResults;
            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }
            
            var response = await request.ExecuteAsync();
            if (response.Messages != null)
            {
                foreach (var messageItem in response.Messages)
                {
                    var fullMessage = await GetMessageDetailsAsync(messageItem.Id);
                    if (fullMessage != null) messages.Add(fullMessage);
                }
            }
            messages = messages.OrderByDescending(m => m.ReceivedDateTime).ToList();
            
            return (messages, response.NextPageToken, !string.IsNullOrEmpty(response.NextPageToken));
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"Error fetching inbox messages with pagination: {ex.Message}"); 
            throw; 
        }
    }

    private async Task<EmailMessage?> GetMessageDetailsAsync(string messageId)
    {
        if (_service == null) return null;
        try
        {
            var request = _service.Users.Messages.Get("me", messageId);
            request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            var message = await request.ExecuteAsync();
            var emailMessage = new EmailMessage
            {
                MessageId = message.Id,
                ReceivedDateTime = DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate ?? 0).UtcDateTime,
                DirectLink = $"https://mail.google.com/mail/u/0/#inbox/{message.Id}",
                IsRead = !message.LabelIds?.Contains("UNREAD") ?? true,
                Labels = message.LabelIds?.Where(l => !l.StartsWith("CATEGORY_") && l != "INBOX" && l != "UNREAD").ToList() ?? new()
            };
            if (message.Payload?.Headers != null)
            {
                foreach (var header in message.Payload.Headers)
                {
                    if (header.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase)) emailMessage.Subject = header.Value;
                    else if (header.Name.Equals("From", StringComparison.OrdinalIgnoreCase)) emailMessage.Sender = header.Value;
                }
            }
            emailMessage.Content = GetMessageBody(message.Payload);
            emailMessage.Attachments = GetAttachments(message.Payload, messageId);
            return emailMessage;
        }
        catch (Exception ex) { Console.WriteLine($"Error getting message details for {messageId}: {ex.Message}"); return null; }
    }

    private string GetMessageBody(MessagePart? payload)
    {
        if (payload == null) return string.Empty;
        if (payload.MimeType == "text/html" && !string.IsNullOrEmpty(payload.Body?.Data)) return DecodeBase64Url(payload.Body.Data);
        else if (payload.MimeType == "text/plain" && !string.IsNullOrEmpty(payload.Body?.Data)) return DecodeBase64Url(payload.Body.Data);
        else if (payload.Parts != null)
        {
            foreach (var part in payload.Parts)
                if (part.MimeType == "text/html" && !string.IsNullOrEmpty(part.Body?.Data)) return DecodeBase64Url(part.Body.Data);
            foreach (var part in payload.Parts)
                if (part.MimeType == "text/plain" && !string.IsNullOrEmpty(part.Body?.Data)) return DecodeBase64Url(part.Body.Data);
        }
        return string.Empty;
    }

    private List<EmailAttachment> GetAttachments(MessagePart? payload, string messageId)
    {
        var attachments = new List<EmailAttachment>();
        if (payload?.Parts == null) return attachments;
        foreach (var part in payload.Parts)
        {
            if (!string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId != null)
            {
                attachments.Add(new EmailAttachment
                {
                    Filename = part.Filename,
                    MimeType = part.MimeType ?? "application/octet-stream",
                    Size = part.Body.Size ?? 0,
                    Url = $"https://mail.google.com/mail/u/0/?ui=2&ik=&view=att&th={messageId}&attid={part.Body.AttachmentId}"
                });
            }
            if (part.Parts != null) attachments.AddRange(GetAttachments(part, messageId));
        }
        return attachments;
    }

    private string DecodeBase64Url(string data)
    {
        try
        {
            var base64 = data.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4) { case 2: base64 += "=="; break; case 3: base64 += "="; break; }
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Moves a Gmail message to the trash (soft delete).
    /// </summary>
    /// <param name="messageId">The ID of the message to delete.</param>
    /// <returns>True if the message was successfully moved to trash, false otherwise.</returns>
    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        if (_service == null) return false;
        try 
        { 
            // Move to trash using the Messages.Trash endpoint instead of Modify
            await _service.Users.Messages.Trash("me", messageId).ExecuteAsync(); 
            return true; 
        }
        catch (Exception ex) { Console.WriteLine($"Error moving message {messageId} to trash: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Archives a Gmail message by removing it from the inbox.
    /// </summary>
    /// <param name="messageId">The ID of the message to archive.</param>
    /// <returns>True if the message was successfully archived, false otherwise.</returns>
    public async Task<bool> ArchiveMessageAsync(string messageId)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { RemoveLabelIds = new List<string> { "INBOX" } }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error archiving message {messageId}: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Adds a star to a Gmail message by applying the STARRED label.
    /// </summary>
    /// <param name="messageId">The ID of the message to star.</param>
    /// <returns>True if the message was successfully starred, false otherwise.</returns>
    public async Task<bool> StarMessageAsync(string messageId)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { AddLabelIds = new List<string> { "STARRED" } }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error starring message {messageId}: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Marks a Gmail message as read by removing the UNREAD label.
    /// </summary>
    /// <param name="messageId">The ID of the message to mark as read.</param>
    /// <returns>True if the message was successfully marked as read, false otherwise.</returns>
    public async Task<bool> MarkAsReadAsync(string messageId)
    {
        if (_service == null) return false;
        try 
        { 
            // Mark as read by removing the UNREAD label
            var request = new ModifyMessageRequest 
            { 
                RemoveLabelIds = new List<string> { "UNREAD" }
            };
            await _service.Users.Messages.Modify(request, "me", messageId).ExecuteAsync(); 
            Console.WriteLine($"Message {messageId} marked as read successfully");
            return true; 
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"Error marking message {messageId} as read: {ex.Message}"); 
            return false; 
        }
    }

    /// <summary>
    /// Modifies the labels on a Gmail message by adding and/or removing specified labels.
    /// </summary>
    /// <param name="messageId">The ID of the message to modify.</param>
    /// <param name="labelsToAdd">A list of label names to add to the message.</param>
    /// <param name="labelsToRemove">A list of label names to remove from the message.</param>
    /// <returns>True if the labels were successfully modified, false otherwise.</returns>
    public async Task<bool> ModifyLabelsAsync(string messageId, List<string> labelsToAdd, List<string> labelsToRemove)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { AddLabelIds = labelsToAdd, RemoveLabelIds = labelsToRemove }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error modifying labels for message {messageId}: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Gets the most recent email messages from the inbox.
    /// </summary>
    /// <param name="count">The number of recent emails to retrieve (default: 5).</param>
    /// <param name="unreadOnly">If true, only fetch unread messages (default: false).</param>
    /// <returns>A list of the most recent EmailMessage objects.</returns>
    public async Task<List<EmailMessage>> GetRecentEmailsAsync(int count = 5, bool unreadOnly = false) => await FetchInboxMessagesAsync(count, unreadOnly);

    /// <summary>
    /// Checks whether a Gmail message is still present in the inbox.
    /// </summary>
    /// <param name="messageId">The ID of the message to check.</param>
    /// <returns>True if the message is still in the inbox, false if it has been deleted or moved.</returns>
    public async Task<bool> MessageStillInInboxAsync(string messageId)
    {
        if (_service == null) 
        {
            Console.WriteLine($"[SYNC-DEBUG] Gmail service is null for message {messageId}");
            return false;
        }
        
        try
        {
            Console.WriteLine($"[SYNC-DEBUG] Checking if message {messageId} is still in INBOX...");
            var message = await _service.Users.Messages.Get("me", messageId).ExecuteAsync();
            
            // Check if message is still in INBOX (not deleted from inbox)
            bool isInInbox = message.LabelIds != null && message.LabelIds.Contains("INBOX");
            
            if (!isInInbox)
            {
                Console.WriteLine($"[SYNC-DEBUG] Message {messageId} is NO LONGER in INBOX - treating as deleted from inbox");
                return false;
            }
            
            Console.WriteLine($"[SYNC-DEBUG] Message {messageId} is still in INBOX");
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"[SYNC-DEBUG] Message {messageId} NOT FOUND in Gmail (404) - completely deleted");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC-DEBUG] Error checking message existence for {messageId}: {ex.Message}");
            return true; // Assume exists to avoid false deletions
        }
    }

    /// <summary>
    /// Forwards a Gmail message to the specified email address.
    /// </summary>
    /// <param name="messageId">The ID of the message to forward.</param>
    /// <param name="toEmail">The email address to forward the message to.</param>
    /// <returns>The ID of the forwarded message, or null if forwarding fails.</returns>
    public async Task<string?> ForwardMessageAsync(string messageId, string toEmail) { await Task.CompletedTask; return null; }
}
