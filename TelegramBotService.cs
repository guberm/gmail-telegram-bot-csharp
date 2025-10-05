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
    private readonly OAuthService _oauthService;
    private readonly AppSettings _settings;
    private long _chatId;

    public TelegramBotService(string botToken, GmailClient gmailService, DatabaseService databaseService, OAuthService oauthService, AppSettings settings)
    {
        _botClient = new TelegramBotClient(botToken);
        _gmailService = gmailService;
        _databaseService = databaseService;
        _oauthService = oauthService;
        _settings = settings;
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
            if (update.Type == UpdateType.Message && update.Message?.Text != null) 
                await HandleMessageAsync(update.Message, cancellationToken);
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null) 
                await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken);
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"Error handling update: {ex.Message}"); 
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        _chatId = message.Chat.Id;
        var command = message.Text?.ToLower().Trim();
        
        switch (command)
        {
            case "/start":
                await HandleStartCommand(cancellationToken);
                break;
            case "/status":
                await HandleStatusCommand(cancellationToken);
                break;
            case "/disconnect":
                await HandleDisconnectCommand(cancellationToken);
                break;
            case "/help":
                await HandleHelpCommand(cancellationToken);
                break;
            default:
                await _botClient.SendTextMessageAsync(_chatId, 
                    "❓ Unknown command. Type /help to see available commands.", 
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleStartCommand(CancellationToken cancellationToken)
    {
        // Check if user already has credentials
        if (_databaseService.HasUserCredentials(_chatId))
        {
            var credentials = _databaseService.GetUserCredentials(_chatId);
            await _botClient.SendTextMessageAsync(_chatId, 
                $"✅ You're already connected!\n\n" +
                $"📧 Gmail account: {credentials?.EmailAddress ?? "Unknown"}\n" +
                $"🕒 Connected since: {credentials?.CreatedAt:yyyy-MM-dd HH:mm} UTC\n\n" +
                $"Your emails will be forwarded here automatically. Use /status to check connection details.", 
                cancellationToken: cancellationToken);
            return;
        }

        // Generate OAuth URL and send authentication link
        if (string.IsNullOrEmpty(_settings.GoogleClientId))
        {
            await _botClient.SendTextMessageAsync(_chatId,
                "❌ Bot configuration error: Google OAuth credentials not configured. Please contact the bot administrator.",
                cancellationToken: cancellationToken);
            return;
        }

        var authUrl = _oauthService.GenerateAuthorizationUrl(_chatId, _settings.GoogleClientId);
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithUrl("🔐 Connect Gmail Account", authUrl)
        });

        await _botClient.SendTextMessageAsync(_chatId,
            "👋 Welcome to Gmail Telegram Bot!\n\n" +
            "To get started, you need to connect your Gmail account. Click the button below to authorize access:\n\n" +
            "🔒 Your credentials are stored securely and only you can access them.\n" +
            "📧 Once connected, new emails will be forwarded here automatically.\n" +
            "⚡ You can disconnect anytime using /disconnect",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleStatusCommand(CancellationToken cancellationToken)
    {
        if (!_databaseService.HasUserCredentials(_chatId))
        {
            await _botClient.SendTextMessageAsync(_chatId,
                "❌ No Gmail account connected.\n\n" +
                "Use /start to connect your Gmail account.",
                cancellationToken: cancellationToken);
            return;
        }

        var credentials = _databaseService.GetUserCredentials(_chatId);
        if (credentials == null)
        {
            await _botClient.SendTextMessageAsync(_chatId,
                "❌ Error retrieving your credentials. Please try /disconnect and then /start again.",
                cancellationToken: cancellationToken);
            return;
        }

        var isExpired = credentials.ExpiresAt <= DateTime.UtcNow;
        var status = isExpired ? "🟡 Token expired (will auto-refresh)" : "🟢 Active";
        
        await _botClient.SendTextMessageAsync(_chatId,
            $"📊 **Gmail Connection Status**\n\n" +
            $"📧 Email: {credentials.EmailAddress}\n" +
            $"🔗 Status: {status}\n" +
            $"🕒 Connected: {credentials.CreatedAt:yyyy-MM-dd HH:mm} UTC\n" +
            $"🔄 Last updated: {credentials.UpdatedAt:yyyy-MM-dd HH:mm} UTC\n" +
            $"⏰ Token expires: {credentials.ExpiresAt:yyyy-MM-dd HH:mm} UTC\n\n" +
            $"💡 Use /disconnect to revoke access\n" +
            $"💡 Use /help for more commands",
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task HandleDisconnectCommand(CancellationToken cancellationToken)
    {
        if (!_databaseService.HasUserCredentials(_chatId))
        {
            await _botClient.SendTextMessageAsync(_chatId,
                "❌ No Gmail account is currently connected.\n\n" +
                "Use /start to connect your Gmail account.",
                cancellationToken: cancellationToken);
            return;
        }

        var credentials = _databaseService.GetUserCredentials(_chatId);
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Yes, Disconnect", "disconnect_confirm"),
                InlineKeyboardButton.WithCallbackData("❌ Cancel", "disconnect_cancel")
            }
        });

        await _botClient.SendTextMessageAsync(_chatId,
            $"⚠️ **Confirm Disconnection**\n\n" +
            $"Are you sure you want to disconnect your Gmail account?\n\n" +
            $"📧 Account: {credentials?.EmailAddress ?? "Unknown"}\n\n" +
            $"This will:\n" +
            $"• Stop forwarding new emails\n" +
            $"• Delete your stored credentials\n" +
            $"• Require re-authentication to reconnect\n\n" +
            $"You can always reconnect later using /start",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleHelpCommand(CancellationToken cancellationToken)
    {
        var helpText = """
            📚 **Gmail Telegram Bot - Help**

            **Available Commands:**
            /start - Connect your Gmail account via OAuth
            /status - Check your Gmail connection status  
            /disconnect - Revoke access and delete credentials
            /help - Show this help message

            **How it works:**
            1️⃣ Use /start to connect your Gmail account
            2️⃣ Authorize the bot in your browser
            3️⃣ New emails will be forwarded here automatically
            4️⃣ Use action buttons on each email to manage them

            **Email Actions:**
            🗑️ Delete - Move email to trash
            📦 Archive - Remove from inbox (keep in All Mail)  
            ⭐ Star - Add star to email
            ➡️ Forward - Forward email to another address

            **Security:**
            🔒 Your credentials are stored securely
            🔑 OAuth tokens are encrypted in local database
            🚫 No passwords are stored
            ⏰ Tokens auto-refresh as needed
            🛡️ You can revoke access anytime

            **Need help?** Check the documentation or report issues on GitHub.
            """;

        await _botClient.SendTextMessageAsync(_chatId,
            helpText,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    public async Task HandleOAuthSuccess(long chatId, string emailAddress, CancellationToken cancellationToken)
    {
        await _botClient.SendTextMessageAsync(chatId,
            $"✅ **Gmail Connected Successfully!**\n\n" +
            $"📧 Account: {emailAddress}\n" +
            $"🕒 Connected: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n\n" +
            $"🎉 You're all set! New emails will now be forwarded to this chat automatically.\n\n" +
            $"💡 Use /status to check connection details\n" +
            $"💡 Use /help for available commands",
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data == null || callbackQuery.Message == null) return;

        // Handle disconnect confirmation
        if (callbackQuery.Data == "disconnect_confirm")
        {
            var credentials = _databaseService.GetUserCredentials(callbackQuery.From.Id);
            _databaseService.DeleteUserCredentials(callbackQuery.From.Id);
            
            await _botClient.EditMessageTextAsync(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                $"✅ **Gmail Account Disconnected**\n\n" +
                $"📧 Account: {credentials?.EmailAddress ?? "Unknown"}\n" +
                $"🕒 Disconnected: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n\n" +
                $"✅ Your credentials have been deleted\n" +
                $"✅ Email forwarding has been stopped\n\n" +
                $"💡 Use /start to reconnect anytime",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
                
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Disconnected successfully", cancellationToken: cancellationToken);
            return;
        }

        if (callbackQuery.Data == "disconnect_cancel")
        {
            await _botClient.EditMessageTextAsync(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                "❌ **Disconnection Cancelled**\n\n" +
                "Your Gmail account remains connected.\n\n" +
                "💡 Use /status to check connection details",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
                
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Cancelled", cancellationToken: cancellationToken);
            return;
        }

        // Handle email actions
        var parts = callbackQuery.Data.Split('|'); 
        if (parts.Length < 2) return;
        
        var action = parts[0]; 
        var messageId = parts[1]; 
        var userId = callbackQuery.From.Id.ToString(); 
        bool success = false;
        
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
            if (!success && action != "forward") 
                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Action failed. Please try again.", cancellationToken: cancellationToken);
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