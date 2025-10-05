using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGmailBot.Models;
using Newtonsoft.Json;

namespace TelegramGmailBot.Services;

public class TelegramBotService
{
    private readonly TelegramBotClient _botClient;
    private readonly GmailClient _gmailService;
    private readonly DatabaseService _databaseService;
    private long _chatId;

    public TelegramBotService(string botToken, GmailClient gmailService, DatabaseService databaseService)
    {
        _botClient = new TelegramBotClient(botToken);
        _gmailService = gmailService;
        _databaseService = databaseService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await _botClient.GetMeAsync(cancellationToken);
        Console.WriteLine($"Bot started: @{me.Username}");
        _botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, cancellationToken: cancellationToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null) await HandleMessageAsync(update.Message, cancellationToken);
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null) await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken);
        }
        catch (Exception ex) { Console.WriteLine($"Error handling update: {ex.Message}"); }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        _chatId = message.Chat.Id;
        if (message.Text == "/start")
        {
            await _botClient.SendTextMessageAsync(_chatId, "Welcome to Gmail Telegram Bot! Your emails will be forwarded here automatically.", cancellationToken: cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data == null || callbackQuery.Message == null) return;
        var parts = callbackQuery.Data.Split('|'); if (parts.Length < 2) return;
        var action = parts[0]; var messageId = parts[1]; var userId = callbackQuery.From.Id.ToString(); bool success = false;
        try
        {
            switch (action)
            {
                case "delete":
                    success = await _gmailService.DeleteMessageAsync(messageId);
                    if (success)
                    {
                        _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "delete", ActionTimestamp = DateTime.UtcNow, UserId = userId });
                        await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Email deleted successfully", cancellationToken: cancellationToken);
                        await _botClient.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken: cancellationToken);
                    }
                    break;
                case "archive":
                    success = await _gmailService.ArchiveMessageAsync(messageId);
                    if (success)
                    {
                        _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "archive", ActionTimestamp = DateTime.UtcNow, UserId = userId });
                        await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Email archived successfully", cancellationToken: cancellationToken);
                    }
                    break;
                case "star":
                    success = await _gmailService.StarMessageAsync(messageId);
                    if (success)
                    {
                        _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "star", ActionTimestamp = DateTime.UtcNow, UserId = userId });
                        await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Email starred successfully", cancellationToken: cancellationToken);
                    }
                    break;
                case "forward":
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Forward feature coming soon", cancellationToken: cancellationToken);
                    break;
                case "label":
                    if (parts.Length >= 3)
                    {
                        var labelName = parts[2];
                        success = await _gmailService.ModifyLabelsAsync(messageId, new List<string> { labelName }, new List<string>());
                        if (success)
                        {
                            _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "label_change", ActionTimestamp = DateTime.UtcNow, UserId = userId, NewLabelValues = new List<string> { labelName } });
                            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, $"Label {labelName} added successfully", cancellationToken: cancellationToken);
                        }
                    }
                    break;
            }
            if (!success && action != "forward") await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Action failed. Please try again.", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling callback: {ex.Message}");
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "An error occurred. Please try again later.", cancellationToken: cancellationToken);
        }
    }

    public async Task SendEmailAsync(EmailMessage emailMessage, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var messageText = BuildMessageText(emailMessage);
            var keyboard = BuildInlineKeyboard(emailMessage);
            var sentMessage = await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
            emailMessage.TelegramMessageId = sentMessage.MessageId.ToString();
            _databaseService.InsertOrUpdateMessage(emailMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending email to Telegram: {ex.Message}");
            try { await _botClient.SendTextMessageAsync(chatId, $"⚠️ Error delivering email: {emailMessage.Subject}\nPlease check the logs.", cancellationToken: cancellationToken); } catch { }
        }
    }

    private string BuildMessageText(EmailMessage emailMessage)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"<b>📧 {EscapeHtml(emailMessage.Subject)}</b>");
        text.AppendLine($"<b>From:</b> {EscapeHtml(emailMessage.Sender)}");
        text.AppendLine($"<b>Date:</b> {emailMessage.ReceivedDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        if (emailMessage.Labels.Any()) text.AppendLine($"<b>Labels:</b> {string.Join(" ", emailMessage.Labels.Select(l => $"#{l.Replace(" ", "_")}"))}");
        text.AppendLine();
        var content = emailMessage.Content; if (content.Length > 2000) content = content[..2000] + "...";
        text.AppendLine(EscapeHtml(content));
        if (emailMessage.Attachments.Any())
        {
            text.AppendLine();
            text.AppendLine("<b>📎 Attachments:</b>");
            foreach (var attachment in emailMessage.Attachments) text.AppendLine($"• {EscapeHtml(attachment.Filename)} ({FormatFileSize(attachment.Size)})");
        }
        text.AppendLine();
        text.AppendLine($"<a href=\"{emailMessage.DirectLink}\">📬 Open in Gmail</a>");
        return text.ToString();
    }

    private InlineKeyboardMarkup BuildInlineKeyboard(EmailMessage emailMessage)
    {
        var buttons = new List<List<InlineKeyboardButton>>
        {
            new() {
                InlineKeyboardButton.WithCallbackData("🗑️ Delete", $"delete|{emailMessage.MessageId}"),
                InlineKeyboardButton.WithCallbackData("📦 Archive", $"archive|{emailMessage.MessageId}"),
                InlineKeyboardButton.WithCallbackData("⭐ Star", $"star|{emailMessage.MessageId}")
            },
            new() { InlineKeyboardButton.WithCallbackData("➡️ Forward", $"forward|{emailMessage.MessageId}") }
        };
        return new InlineKeyboardMarkup(buttons);
    }

    private string EscapeHtml(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" }; double len = bytes; int order = 0; while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; } return $"{len:0.##} {sizes[order]}";
    }
    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) { Console.WriteLine($"Telegram Bot Error: {exception.Message}"); return Task.CompletedTask; }
    public async Task NotifyErrorAsync(string errorMessage, CancellationToken cancellationToken)
    { if (_chatId != 0) { try { await _botClient.SendTextMessageAsync(_chatId, $"⚠️ Error: {errorMessage}", cancellationToken: cancellationToken); } catch { } } }
    public void SetChatId(long chatId) => _chatId = chatId;
}
