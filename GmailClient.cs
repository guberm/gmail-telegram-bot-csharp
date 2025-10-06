using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using TelegramGmailBot.Models;
using System.Text;

namespace TelegramGmailBot.Services;

public class GmailClient
{
    private readonly AppSettings _settings;
    private Google.Apis.Gmail.v1.GmailService? _service;
    private long _currentChatId;

    public GmailClient(AppSettings settings) { _settings = settings; }

    public async Task<bool> AuthenticateAsync(string accessToken, string refreshToken)
    {
        try
        {
            var tokenResponse = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };

            var credential = new UserCredential(
                new GoogleAuthorizationCodeFlow(
                    new GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = new ClientSecrets
                        {
                            ClientId = _settings.GoogleClientId,
                            ClientSecret = _settings.GoogleClientSecret
                        }
                    }),
                "user",
                tokenResponse);

            _service = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = _settings.ApplicationName
            });

            Console.WriteLine("Gmail authentication successful");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gmail authentication failed: {ex.Message}");
            return false;
        }
    }

    public void SetCurrentUser(long chatId)
    {
        _currentChatId = chatId;
    }

    public async Task<List<EmailMessage>> FetchInboxMessagesAsync(int maxResults = 10)
    {
        if (_service == null) throw new InvalidOperationException("Gmail service not authenticated");
        var messages = new List<EmailMessage>();
        try
        {
            var request = _service.Users.Messages.List("me");
            request.LabelIds = new Google.Apis.Util.Repeatable<string>(new[] { "INBOX" });
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

    public async Task<bool> ArchiveMessageAsync(string messageId)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { RemoveLabelIds = new List<string> { "INBOX" } }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error archiving message {messageId}: {ex.Message}"); return false; }
    }

    public async Task<bool> StarMessageAsync(string messageId)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { AddLabelIds = new List<string> { "STARRED" } }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error starring message {messageId}: {ex.Message}"); return false; }
    }

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

    public async Task<bool> ModifyLabelsAsync(string messageId, List<string> labelsToAdd, List<string> labelsToRemove)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { AddLabelIds = labelsToAdd, RemoveLabelIds = labelsToRemove }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error modifying labels for message {messageId}: {ex.Message}"); return false; }
    }

    public async Task<List<EmailMessage>> GetRecentEmailsAsync(int count = 5) => await FetchInboxMessagesAsync(count);

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

    public async Task<string?> ForwardMessageAsync(string messageId, string toEmail) { await Task.CompletedTask; return null; }
}
