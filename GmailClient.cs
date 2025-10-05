using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using TelegramGmailBot.Models;
using System.Text;

namespace TelegramGmailBot.Services;

public class GmailClient
{
    private readonly AppSettings _settings;
    private Google.Apis.Gmail.v1.GmailService? _service;
    private UserCredential? _credential;

    public GmailClient(AppSettings settings) { _settings = settings; }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            var clientSecrets = new ClientSecrets { ClientId = _settings.GmailClientId, ClientSecret = _settings.GmailClientSecret };
            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets, _settings.GmailScopes, "user", CancellationToken.None, new FileDataStore("token.json", true));
            _service = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer { HttpClientInitializer = _credential, ApplicationName = _settings.ApplicationName });
            Console.WriteLine("Gmail authentication successful");
            return true;
        }
        catch (Exception ex) { Console.WriteLine($"Gmail authentication failed: {ex.Message}"); return false; }
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
        try { await _service.Users.Messages.Delete("me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error deleting message {messageId}: {ex.Message}"); return false; }
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
    public async Task<bool> ModifyLabelsAsync(string messageId, List<string> labelsToAdd, List<string> labelsToRemove)
    {
        if (_service == null) return false;
        try { await _service.Users.Messages.Modify(new ModifyMessageRequest { AddLabelIds = labelsToAdd, RemoveLabelIds = labelsToRemove }, "me", messageId).ExecuteAsync(); return true; }
        catch (Exception ex) { Console.WriteLine($"Error modifying labels for message {messageId}: {ex.Message}"); return false; }
    }
    public async Task<string?> ForwardMessageAsync(string messageId, string toEmail) { await Task.CompletedTask; return null; }
}
