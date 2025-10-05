using Newtonsoft.Json;

namespace TelegramGmailBot.Models;

public class EmailMessage
{
    [JsonProperty("message_id")] public string MessageId { get; set; } = string.Empty;
    [JsonProperty("subject")] public string Subject { get; set; } = string.Empty;
    [JsonProperty("sender")] public string Sender { get; set; } = string.Empty;
    [JsonProperty("received_datetime")] public DateTime ReceivedDateTime { get; set; }
    [JsonProperty("content")] public string Content { get; set; } = string.Empty;
    [JsonProperty("attachments")] public List<EmailAttachment> Attachments { get; set; } = new();
    [JsonProperty("labels")] public List<string> Labels { get; set; } = new();
    [JsonProperty("direct_link")] public string DirectLink { get; set; } = string.Empty;
    [JsonProperty("is_read")] public bool IsRead { get; set; }
    [JsonProperty("telegram_message_id")] public string? TelegramMessageId { get; set; }
}

public class EmailAttachment
{
    [JsonProperty("filename")] public string Filename { get; set; } = string.Empty;
    [JsonProperty("url")] public string? Url { get; set; }
    [JsonProperty("mime_type")] public string MimeType { get; set; } = string.Empty;
    [JsonProperty("size")] public long Size { get; set; }
}

public class MessageAction
{
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("message_id")] public string MessageId { get; set; } = string.Empty;
    [JsonProperty("action_type")] public string ActionType { get; set; } = string.Empty;
    [JsonProperty("action_timestamp")] public DateTime ActionTimestamp { get; set; }
    [JsonProperty("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonProperty("new_label_values")] public List<string>? NewLabelValues { get; set; }
}

public class TelegramEmailMessage
{
    [JsonProperty("subject")] public string Subject { get; set; } = string.Empty;
    [JsonProperty("from")] public string From { get; set; } = string.Empty;
    [JsonProperty("received_datetime")] public string ReceivedDateTime { get; set; } = string.Empty;
    [JsonProperty("content_html")] public string ContentHtml { get; set; } = string.Empty;
    [JsonProperty("attachments")] public List<EmailAttachment> Attachments { get; set; } = new();
    [JsonProperty("labels")] public List<string> Labels { get; set; } = new();
    [JsonProperty("direct_link")] public string DirectLink { get; set; } = string.Empty;
    [JsonProperty("action_buttons")] public List<ActionButton> ActionButtons { get; set; } = new();
}

public class ActionButton
{
    [JsonProperty("type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("label")] public string Label { get; set; } = string.Empty;
}
