using Telegram.Bot.Extensions.Markup;
using Markdig;
using TelegramGmailBot.Models;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace TelegramGmailBot.Services;

/// <summary>
/// Provides advanced formatting capabilities for Telegram messages using specialized libraries.
/// </summary>
public class TelegramMessageFormatter
{
    private readonly MarkdownPipeline _markdownPipeline;

    /// <summary>
    /// Initializes a new instance of the TelegramMessageFormatter with configured markdown pipeline.
    /// </summary>
    public TelegramMessageFormatter()
    {
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }



    /// <summary>
    /// Builds a professionally formatted Telegram message from an email.
    /// </summary>
    /// <param name="emailMessage">The email message to format.</param>
    /// <returns>A formatted HTML string suitable for Telegram.</returns>
    public string BuildFormattedMessage(EmailMessage emailMessage)
    {
        var formatter = new TelegramMessageBuilder();

        // Subject section with emphasis
        formatter.AddSection("📧 Email", emailMessage.Subject, isHeader: true);

        // Sender and date information
        formatter.AddInfoLine("👤 From", emailMessage.Sender, useCodeForValue: true);
        formatter.AddInfoLine("📅 Date", emailMessage.ReceivedDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC", useCodeForValue: true);

        // Read status
        var statusIcon = emailMessage.IsRead ? "✅" : "🔵";
        var statusText = emailMessage.IsRead ? "Read" : "Unread";
        formatter.AddInfoLine("📖 Status", $"{statusIcon} {statusText}");

        // Labels section
        if (emailMessage.Labels.Any())
        {
            formatter.AddLabelsSection(emailMessage.Labels);
        }

        // Content section
        var processedContent = ProcessEmailContent(emailMessage.Content);
        if (!string.IsNullOrEmpty(processedContent))
        {
            formatter.AddContentSection(processedContent);
        }

        // Attachments section
        if (emailMessage.Attachments.Any())
        {
            formatter.AddAttachmentsSection(emailMessage.Attachments);
        }

        // Footer with Gmail link
        formatter.AddFooter(emailMessage.DirectLink);

        return formatter.Build();
    }

    /// <summary>
    /// Builds a short formatted message for emails that are too long for Telegram.
    /// </summary>
    /// <param name="emailMessage">The email message to format.</param>
    /// <returns>A formatted HTML string for a truncated message.</returns>
    public string BuildShortMessage(EmailMessage emailMessage)
    {
        var formatter = new TelegramMessageBuilder();

        // Subject section
        formatter.AddSection("📧 Email", emailMessage.Subject, isHeader: true);

        // Basic info
        formatter.AddInfoLine("👤 From", emailMessage.Sender, useCodeForValue: true);
        formatter.AddInfoLine("📅 Date", emailMessage.ReceivedDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC", useCodeForValue: true);

        // Read status
        var statusIcon = emailMessage.IsRead ? "✅" : "🔵";
        var statusText = emailMessage.IsRead ? "Read" : "Unread";
        formatter.AddInfoLine("📖 Status", $"{statusIcon} {statusText}");

        // Labels if present
        if (emailMessage.Labels.Any())
        {
            formatter.AddLabelsSection(emailMessage.Labels);
        }

        // Too long notice
        formatter.AddWarningSection("⚠️ Message Too Long", 
            "This email is too large to display in Telegram. Please use the link below to read the full content.");

        // Footer
        formatter.AddFooter(emailMessage.DirectLink, "Open in Gmail to read full message");

        return formatter.Build();
    }

    /// <summary>
    /// Builds a minimal notification message for failed formatting attempts.
    /// </summary>
    /// <param name="emailMessage">The email message to format.</param>
    /// <returns>A minimal formatted HTML string.</returns>
    public string BuildMinimalMessage(EmailMessage emailMessage)
    {
        var formatter = new TelegramMessageBuilder();

        formatter.AddSection("📧 New Email", "", isHeader: true);
        formatter.AddInfoLine("Subject", emailMessage.Subject, useCodeForValue: true);
        formatter.AddInfoLine("From", GetSenderName(emailMessage.Sender), useCodeForValue: true);
        formatter.AddWarningSection("⚠️ Display Error", "Unable to display full content");
        formatter.AddFooter(emailMessage.DirectLink);

        return formatter.Build();
    }

    private string ProcessEmailContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // Convert HTML to clean text with preserved structure
        var cleanText = CleanHtmlContent(content);
        
        // Limit content length
        if (cleanText.Length > 1800)
        {
            cleanText = cleanText[..1800] + "...";
        }

        // Return clean text directly - TelegramMessageBuilder will handle HTML formatting
        return cleanText;
    }

    private string CleanHtmlContent(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove script, style, and head tags with content
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "", 
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert links to readable format: <a href="url">text</a> -> text: url
        html = Regex.Replace(html, @"<a[^>]*href=[""']([^""']*)[""'][^>]*>(.*?)</a>", 
            (match) => {
                var url = match.Groups[1].Value;
                var text = match.Groups[2].Value.Trim();
                
                // If link text is just the URL or very similar, show just the URL
                if (string.IsNullOrEmpty(text) || text == url || text.Contains(url.Replace("https://", "").Replace("http://", "")))
                    return url;
                
                // Otherwise show: text - url
                return $"{text}: {url}";
            }, RegexOptions.IgnoreCase);

        // Convert common HTML elements to text equivalents
        html = Regex.Replace(html, @"<br\s*/?>\s*", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|h[1-6]|li)\s*>", "\n\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        
        // Convert headers to emphasize with line breaks
        html = Regex.Replace(html, @"<h[1-6][^>]*>", "\n\n** ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</h[1-6]>", " **\n\n", RegexOptions.IgnoreCase);

        // Remove all other HTML tags
        html = Regex.Replace(html, @"<[^>]+>", " ");

        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);

        // Clean up whitespace
        html = Regex.Replace(html, @"[ \t]+", " ");
        html = Regex.Replace(html, @"\n[ \t]*\n", "\n\n");
        html = Regex.Replace(html, @"\n{3,}", "\n\n");

        return html.Trim();
    }



    private string GetSenderName(string sender)
    {
        if (string.IsNullOrEmpty(sender))
            return "Unknown";

        // Extract name part from "Name <email>" format
        var match = Regex.Match(sender, @"^(.+?)\s*<");
        return match.Success ? match.Groups[1].Value.Trim() : sender.Split('<')[0].Trim();
    }
}

/// <summary>
/// Builder class for constructing Telegram messages with consistent formatting.
/// </summary>
public class TelegramMessageBuilder
{
    private readonly StringBuilder _content;
    private bool _hasContent;

    /// <summary>
    /// Initializes a new instance of the TelegramMessageBuilder.
    /// </summary>
    public TelegramMessageBuilder()
    {
        _content = new StringBuilder();
        _hasContent = false;
    }

    /// <summary>
    /// Escapes HTML entities for safe display in Telegram HTML mode.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>HTML-escaped text safe for Telegram.</returns>
    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return HttpUtility.HtmlEncode(text);
    }

    /// <summary>
    /// Adds a section with a title and optional content to the message.
    /// </summary>
    /// <param name="title">The section title.</param>
    /// <param name="content">The section content.</param>
    /// <param name="isHeader">Whether to format as a header with blockquote styling.</param>
    public void AddSection(string title, string content, bool isHeader = false)
    {
        if (_hasContent)
            _content.AppendLine();

        if (isHeader)
        {
            _content.AppendLine($"<blockquote><b>{EscapeHtml(title)}</b>");
            if (!string.IsNullOrEmpty(content))
            {
                _content.AppendLine($"{EscapeHtml(content)}");
            }
            _content.AppendLine("</blockquote>");
        }
        else
        {
            _content.AppendLine($"<b>{EscapeHtml(title)}</b>");
            if (!string.IsNullOrEmpty(content))
            {
                _content.AppendLine(EscapeHtml(content));
            }
        }

        _hasContent = true;
    }

    /// <summary>
    /// Adds an information line with a label and value to the message.
    /// </summary>
    /// <param name="label">The label for the information.</param>
    /// <param name="value">The value to display.</param>
    /// <param name="useCodeForValue">Whether to format the value as code.</param>
    public void AddInfoLine(string label, string value, bool useCodeForValue = false)
    {
        if (_hasContent)
            _content.AppendLine();

        var formattedValue = useCodeForValue 
            ? $"<code>{EscapeHtml(value)}</code>"
            : EscapeHtml(value);

        _content.AppendLine($"<b>{EscapeHtml(label)}:</b> {formattedValue}");
        _hasContent = true;
    }

    /// <summary>
    /// Adds a labels section with formatted hashtags to the message.
    /// </summary>
    /// <param name="labels">The list of labels to display.</param>
    public void AddLabelsSection(List<string> labels)
    {
        if (!labels.Any()) return;

        if (_hasContent)
            _content.AppendLine();

        _content.AppendLine("<b>🏷️ Labels:</b>");
        
        // Group labels in sets of 3 for better readability
        for (int i = 0; i < labels.Count; i += 3)
        {
            var labelGroup = labels.Skip(i).Take(3)
                .Select(l => $"<code>#{EscapeHtml(l.Replace(" ", "_"))}</code>");
            _content.AppendLine($"  {string.Join(" ", labelGroup)}");
        }

        _hasContent = true;
    }

    /// <summary>
    /// Adds a content section with formatted email content to the message.
    /// </summary>
    /// <param name="content">The email content to display.</param>
    public void AddContentSection(string content)
    {
        if (string.IsNullOrEmpty(content)) return;

        if (_hasContent)
            _content.AppendLine();

        _content.AppendLine("<b>📄 Content:</b>");
        //_content.AppendLine("<pre>══════════════</pre>");
        _content.AppendLine();
        
        // First paragraph gets blockquote treatment for emphasis
        var paragraphs = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (paragraphs.Length > 0)
        {
            // Use blockquote for the first paragraph to add visual emphasis
            _content.AppendLine($"<blockquote>{EscapeHtml(paragraphs[0].Trim())}</blockquote>");

            // Add remaining paragraphs with proper spacing
            for (int i = 1; i < Math.Min(paragraphs.Length, 3); i++)  // Limit to 3 paragraphs
            {
                if (!string.IsNullOrWhiteSpace(paragraphs[i]))
                {
                    _content.AppendLine();
                    _content.AppendLine(EscapeHtml(paragraphs[i].Trim()));
                }
            }

            if (paragraphs.Length > 3)
            {
                //_content.AppendLine();
                //_content.AppendLine("<i>📖 Content continues...</i>");
            }
        }

        _hasContent = true;
    }

    /// <summary>
    /// Adds an attachments section with file information to the message.
    /// </summary>
    /// <param name="attachments">The list of email attachments to display.</param>
    public void AddAttachmentsSection(List<EmailAttachment> attachments)
    {
        if (!attachments.Any()) return;

        if (_hasContent)
            _content.AppendLine();

        //_content.AppendLine("<pre>══════════════</pre>");
        _content.AppendLine("<b>📎 Attachments:</b>");

        foreach (var attachment in attachments)
        {
            var size = FormatFileSize(attachment.Size);
            _content.AppendLine($"  <code>📁 {EscapeHtml(attachment.Filename)}</code> <i>({size})</i>");
        }

        _hasContent = true;
    }

    /// <summary>
    /// Adds a warning section with a title and message to highlight important information.
    /// </summary>
    /// <param name="title">The warning title.</param>
    /// <param name="message">The warning message.</param>
    public void AddWarningSection(string title, string message)
    {
        if (_hasContent)
            _content.AppendLine();

        //_content.AppendLine("<pre>══════════════</pre>");
        _content.AppendLine($"<blockquote><b>{EscapeHtml(title)}</b></blockquote>");
        _content.AppendLine($"<i>{EscapeHtml(message)}</i>");

        _hasContent = true;
    }

    /// <summary>
    /// Adds a footer section with a Gmail link.
    /// </summary>
    /// <param name="gmailLink">The Gmail link URL.</param>
    /// <param name="linkText">The text to display for the link.</param>
    public void AddFooter(string gmailLink, string linkText = "Open in Gmail")
    {
        if (_hasContent)
            _content.AppendLine();

        //_content.AppendLine("<pre>══════════════</pre>");
        _content.AppendLine($"<b>📬 <a href=\"{gmailLink}\">{EscapeHtml(linkText)}</a></b>");

        _hasContent = true;
    }

    /// <summary>
    /// Builds and returns the formatted message string.
    /// </summary>
    /// <returns>The complete formatted message as HTML string.</returns>
    public string Build()
    {
        return _content.ToString();
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}