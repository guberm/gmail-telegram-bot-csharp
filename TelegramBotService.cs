using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using TelegramGmailBot.Models;
using Newtonsoft.Json;

namespace TelegramGmailBot.Services;

/// <summary>
/// Service for handling interactions with the Telegram Bot API.
/// </summary>
public class TelegramBotService
{
    private readonly TelegramBotClient _botClient;
    private readonly GmailClient _gmailService;
    private readonly DatabaseService _databaseService;
    private readonly OAuthService _oauthService;
    private readonly AppSettings _settings;
    private readonly TelegramMessageFormatter _messageFormatter;
    private long _chatId;

    /// <summary>
    /// Initializes a new instance of the TelegramBotService.
    /// </summary>
    /// <param name="botToken">The Telegram bot token.</param>
    /// <param name="gmailService">The Gmail client service for accessing email data.</param>
    /// <param name="databaseService">The database service for storing email information.</param>
    /// <param name="oauthService">The OAuth service for handling OAuth operations.</param>
    /// <param name="settings">The application settings containing bot configuration.</param>
    public TelegramBotService(string botToken, GmailClient gmailService, DatabaseService databaseService, OAuthService oauthService, AppSettings settings)
    {
        _botClient = new TelegramBotClient(botToken);
        _gmailService = gmailService;
        _databaseService = databaseService;
        _oauthService = oauthService;
        _settings = settings;
        _messageFormatter = new TelegramMessageFormatter();
    }

    /// <summary>
    /// Starts the Telegram bot service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to stop the service.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await _botClient.GetMe(cancellationToken);
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
        var messageText = message.Text?.Trim();
        var command = messageText?.ToLower().Split(' ')[0];

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
            case "/emails":
                await HandleEmailsCommand(messageText, cancellationToken);
                break;
            case "/test_sync":
                await HandleTestSyncCommand(messageText ?? string.Empty, cancellationToken);
                break;
            case "/cleanup_sync":
                await HandleCleanupSyncCommand(cancellationToken);
                break;
            default:
                await _botClient.SendMessage(_chatId,
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
            await _botClient.SendMessage(_chatId,
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
            await _botClient.SendMessage(_chatId,
                "❌ Bot configuration error: Google OAuth credentials not configured. Please contact the bot administrator.",
                cancellationToken: cancellationToken);
            return;
        }

        var authUrl = _oauthService.GenerateAuthorizationUrl(_chatId, _settings.GoogleClientId);
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithUrl("🔐 Connect Gmail Account", authUrl)
        });

        await _botClient.SendMessage(_chatId,
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
            await _botClient.SendMessage(_chatId,
                "❌ No Gmail account connected.\n\n" +
                "Use /start to connect your Gmail account.",
                cancellationToken: cancellationToken);
            return;
        }

        var credentials = _databaseService.GetUserCredentials(_chatId);
        if (credentials == null)
        {
            await _botClient.SendMessage(_chatId,
                "❌ Error retrieving your credentials. Please try /disconnect and then /start again.",
                cancellationToken: cancellationToken);
            return;
        }

        var isExpired = credentials.ExpiresAt <= DateTime.UtcNow;
        var status = isExpired ? "🟡 Token expired (will auto-refresh)" : "🟢 Active";

        await _botClient.SendMessage(_chatId,
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
            await _botClient.SendMessage(_chatId,
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

        await _botClient.SendMessage(_chatId,
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
            /emails [count] - Fetch recent emails (default: 5, max: 20)
            /disconnect - Revoke access and delete credentials
            /test_sync <message_id> - Test sync status for specific message
            /cleanup_sync - Clean up orphaned database entries
            /help - Show this help message

            **How it works:**
            1️⃣ Use /start to connect your Gmail account
            2️⃣ Authorize the bot in your browser
            3️⃣ New emails will be forwarded here automatically
            4️⃣ Use action buttons on each email to manage them
            5️⃣ Emails deleted in Gmail will auto-sync to Telegram

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

            **Email Synchronization:**
            🔄 Auto-sync when emails are deleted in Gmail
            🗑️ Telegram messages will be removed automatically
            ⚡ Real-time synchronization during polling

            **Need help?** Check the documentation or report issues on GitHub.
            """;

        await _botClient.SendMessage(_chatId,
            helpText,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task HandleEmailsCommand(string? messageText, CancellationToken cancellationToken)
    {
        // Check if user is authenticated
        if (!_databaseService.HasUserCredentials(_chatId))
        {
            await _botClient.SendMessage(_chatId,
                "❌ No Gmail account connected.\n\n" +
                "Use /start to connect your Gmail account first.",
                cancellationToken: cancellationToken);
            return;
        }

        // Parse count parameter (default 5, max 20)
        int count = 5;
        if (!string.IsNullOrEmpty(messageText))
        {
            var parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && int.TryParse(parts[1], out int parsedCount))
            {
                count = Math.Clamp(parsedCount, 1, 20);
            }
        }

        try
        {
            await _botClient.SendMessage(_chatId,
                $"🔍 Fetching your last {count} email(s)...",
                cancellationToken: cancellationToken);

            // Fetch recent emails from Gmail
            var emails = await _gmailService.GetRecentEmailsAsync(count);

            if (emails == null || !emails.Any())
            {
                await _botClient.SendMessage(_chatId,
                    "📭 No emails found in your inbox.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Send each email
            foreach (var email in emails)
            {
                await SendEmailAsync(email, _chatId, cancellationToken);
                await Task.Delay(500, cancellationToken); // Small delay to avoid rate limits
            }

            await _botClient.SendMessage(_chatId,
                $"✅ Sent {emails.Count} email(s).",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching emails: {ex.Message}");
            await _botClient.SendMessage(_chatId,
                $"❌ Error fetching emails: {ex.Message}\n\n" +
                "Please check your Gmail connection with /status",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleTestSyncCommand(string messageText, CancellationToken cancellationToken)
    {
        try
        {
            var parts = messageText.Split(' ');
            if (parts.Length < 2)
            {
                await _botClient.SendMessage(_chatId,
                    "❓ Usage: `/test_sync <message_id>`\n\n" +
                    "Example: `/test_sync 199b981d77b0cc02`\n\n" +
                    "This will test if a specific Gmail message still exists.",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            var messageId = parts[1].Trim();

            await _botClient.SendMessage(_chatId,
                $"🔍 Testing sync for message ID: `{messageId}`\n\n" +
                "Checking if message is still in INBOX...",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);

            // Check if message is still in INBOX
            var stillInInbox = await _gmailService.MessageStillInInboxAsync(messageId);

            // Check if message exists in database
            var existsInDb = _databaseService.MessageExists(messageId);

            var result = $"📊 **Sync Test Results for {messageId}**\n\n" +
                        $"📧 Still in INBOX: {(stillInInbox ? "✅ Yes" : "❌ No")}\n" +
                        $"🗄️ Exists in Database: {(existsInDb ? "✅ Yes" : "❌ No")}\n\n";

            if (!stillInInbox && existsInDb)
            {
                result += "⚠️ **Sync Issue Detected!**\n" +
                         "Message removed from INBOX but still in database.\n" +
                         "This should be automatically synced in next polling cycle.";
            }
            else if (stillInInbox && !existsInDb)
            {
                result += "ℹ️ **Normal State**\n" +
                         "Message in INBOX but not in database.\n" +
                         "This is normal for messages not yet processed.";
            }
            else if (!stillInInbox && !existsInDb)
            {
                result += "✅ **Synced State**\n" +
                         "Message properly removed from both INBOX and database.";
            }
            else
            {
                result += "✅ **Normal State**\n" +
                         "Message exists in both INBOX and database.";
            }

            await _botClient.SendMessage(_chatId,
                result,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in test sync: {ex.Message}");
            await _botClient.SendMessage(_chatId,
                $"❌ Error testing sync: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleCleanupSyncCommand(CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.SendMessage(_chatId,
                "🧹 Starting database cleanup...\n\n" +
                "Checking all stored messages against Gmail INBOX...",
                cancellationToken: cancellationToken);

            // Get all stored messages for this user
            var allStoredMessages = _databaseService.GetAllMessagesForUser(_chatId);
            var cleanedCount = 0;
            var totalMessages = allStoredMessages.Count;

            await _botClient.SendMessage(_chatId,
                $"📊 Found {totalMessages} messages in database. Starting cleanup...",
                cancellationToken: cancellationToken);

            foreach (var storedMessage in allStoredMessages)
            {
                try
                {
                    // Check if message still exists in Gmail INBOX
                    var stillInInbox = await _gmailService.MessageStillInInboxAsync(storedMessage.MessageId);

                    if (!stillInInbox)
                    {
                        // Message no longer in INBOX, clean it up
                        var dbDeleteSuccess = _databaseService.DeleteMessage(storedMessage.MessageId);
                        if (dbDeleteSuccess)
                        {
                            cleanedCount++;
                            Console.WriteLine($"[CLEANUP] Removed orphaned message '{storedMessage.Subject}' from database");
                        }
                    }

                    // Small delay to avoid hitting Gmail API rate limits
                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CLEANUP] Error checking message {storedMessage.MessageId}: {ex.Message}");
                }
            }

            await _botClient.SendMessage(_chatId,
                $"✅ **Cleanup Complete!**\n\n" +
                $"📊 Total messages checked: {totalMessages}\n" +
                $"🧹 Orphaned messages removed: {cleanedCount}\n" +
                $"💾 Remaining messages: {totalMessages - cleanedCount}\n\n" +
                "Database is now synchronized with Gmail INBOX.",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in cleanup sync: {ex.Message}");
            await _botClient.SendMessage(_chatId,
                $"❌ Error during cleanup: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Handles the successful OAuth connection for a user.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <param name="emailAddress">The email address of the connected Gmail account.</param>
    /// <param name="cancellationToken">The cancellation token to stop the operation.</param>
    public async Task HandleOAuthSuccess(long chatId, string emailAddress, CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(chatId,
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

            await _botClient.EditMessageText(
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

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Disconnected successfully", cancellationToken: cancellationToken);
            return;
        }

        if (callbackQuery.Data == "disconnect_cancel")
        {
            await _botClient.EditMessageText(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                "❌ **Disconnection Cancelled**\n\n" +
                "Your Gmail account remains connected.\n\n" +
                "💡 Use /status to check connection details",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Cancelled", cancellationToken: cancellationToken);
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
                        await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Email deleted successfully", cancellationToken: cancellationToken);
                        await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken: cancellationToken);
                    }
                    break;
                case "archive":
                    success = await _gmailService.ArchiveMessageAsync(messageId);
                    if (success)
                    {
                        _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "archive", ActionTimestamp = DateTime.UtcNow, UserId = userId });
                        await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Email archived successfully", cancellationToken: cancellationToken);
                    }
                    break;
                case "star":
                    success = await _gmailService.StarMessageAsync(messageId);
                    if (success)
                    {
                        _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "star", ActionTimestamp = DateTime.UtcNow, UserId = userId });
                        await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Email starred successfully", cancellationToken: cancellationToken);
                    }
                    break;
                case "forward":
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Forward feature coming soon", cancellationToken: cancellationToken);
                    break;
                case "label":
                    if (parts.Length >= 3)
                    {
                        var labelName = parts[2];
                        success = await _gmailService.ModifyLabelsAsync(messageId, new List<string> { labelName }, new List<string>());
                        if (success)
                        {
                            _databaseService.InsertAction(new MessageAction { MessageId = messageId, ActionType = "label_change", ActionTimestamp = DateTime.UtcNow, UserId = userId, NewLabelValues = new List<string> { labelName } });
                            await _botClient.AnswerCallbackQuery(callbackQuery.Id, $"Label {labelName} added successfully", cancellationToken: cancellationToken);
                        }
                    }
                    break;
            }
            if (!success && action != "forward")
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Action failed. Please try again.", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling callback: {ex.Message}");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, "An error occurred. Please try again later.", cancellationToken: cancellationToken);
        }
    }
    
    /// <summary>
    /// Sends an email to the specified chat.
    /// </summary>
    /// <param name="emailMessage">The email message to send.</param>
    /// <param name="chatId">The chat ID to send the email to.</param>
    /// <param name="cancellationToken">The cancellation token to stop the operation.</param>
    public async Task SendEmailAsync(EmailMessage emailMessage, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var messageText = _messageFormatter.BuildFormattedMessage(emailMessage);
            var keyboard = BuildInlineKeyboard(emailMessage);
            var sentMessage = await _botClient.SendMessage(chatId, messageText, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
            emailMessage.TelegramMessageId = sentMessage.MessageId.ToString();
            _databaseService.InsertOrUpdateMessage(emailMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending email to Telegram: {ex.Message}");

            // If message is too long, send a short version with link to Gmail
            if (ex.Message.Contains("too long") || ex.Message.Contains("message is too long"))
            {
                try
                {
                    var shortMessage = _messageFormatter.BuildShortMessage(emailMessage);
                    var keyboard = BuildInlineKeyboard(emailMessage);
                    var sentMessage = await _botClient.SendMessage(chatId, shortMessage, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
                    emailMessage.TelegramMessageId = sentMessage.MessageId.ToString();
                    _databaseService.InsertOrUpdateMessage(emailMessage);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"Error sending short email to Telegram: {ex2.Message}");
                    await SendMinimalNotification(chatId, emailMessage, cancellationToken);
                }
            }
            else
            {
                await SendMinimalNotification(chatId, emailMessage, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Sends a simple text notification message to a Telegram chat.
    /// </summary>
    /// <param name="message">The message text to send (supports HTML formatting).</param>
    /// <param name="chatId">The Telegram chat ID to send the message to.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    public async Task SendTextNotificationAsync(string message, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.SendMessage(chatId, message, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        catch
        {
            // Fallback to plain text if HTML parsing fails
            try
            {
                await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send text notification to chat {chatId}: {ex.Message}");
            }
        }
    }

    private async Task SendMinimalNotification(long chatId, EmailMessage emailMessage, CancellationToken cancellationToken)
    {
        try
        {
            var minimalMessage = _messageFormatter.BuildMinimalMessage(emailMessage);
            var sentMessage = await _botClient.SendMessage(chatId, minimalMessage, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            
            // Update the database with the Telegram message ID
            emailMessage.TelegramMessageId = sentMessage.MessageId.ToString();
            _databaseService.InsertOrUpdateMessage(emailMessage);
        }
        catch
        {
            // Last resort: plain text only
            try
            {
                var sentMessage = await _botClient.SendMessage(chatId, $"📧 New email from {emailMessage.Sender?.Split('<')[0]?.Trim() ?? "Unknown"} - check Gmail", cancellationToken: cancellationToken);
                
                // Update the database with the Telegram message ID
                emailMessage.TelegramMessageId = sentMessage.MessageId.ToString();
                _databaseService.InsertOrUpdateMessage(emailMessage);
            }
            catch { }
        }
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
        
        return System.Web.HttpUtility.HtmlEncode(text);
    }

    private string BuildMessageText(EmailMessage emailMessage)
    {
        var text = new System.Text.StringBuilder();
        
        // Header with subject using blockquote for emphasis
        text.AppendLine($"<blockquote>📧 <b>{EscapeHtml(emailMessage.Subject)}</b></blockquote>");
        text.AppendLine();
        
        // Sender and date information with better spacing
        text.AppendLine($"<b>👤 From:</b> <code>{EscapeHtml(emailMessage.Sender)}</code>");
        text.AppendLine($"<b>📅 Date:</b> <code>{emailMessage.ReceivedDateTime:yyyy-MM-dd HH:mm:ss} UTC</code>");
        
        // Labels with improved formatting
        if (emailMessage.Labels.Any()) 
        {
            text.AppendLine();
            text.AppendLine("<b>🏷️ Labels:</b>");
            var labelChunks = emailMessage.Labels.Select(l => $"<code>#{EscapeHtml(l.Replace(" ", "_"))}</code>").ToList();
            
            // Display labels in rows of 3 with better spacing
            for (int i = 0; i < labelChunks.Count; i += 3)
            {
                var chunk = labelChunks.Skip(i).Take(3);
                text.AppendLine($"  {string.Join(" ", chunk)}");
            }
        }
        
        // Content section with clear separation
        text.AppendLine();
        text.AppendLine("<b>📄 Content:</b>");
        text.AppendLine("<pre>═══════════════════════════════</pre>");
        
        // Content processing with better paragraph handling
        var content = StripHtmlTags(emailMessage.Content);
        if (content.Length > 2000) content = content[..2000] + "...";
        
        // Split content into paragraphs and format them
        var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var displayedParagraphs = 0;
        
        foreach (var paragraph in paragraphs.Take(3))
        {
            var cleanParagraph = paragraph.Trim();
            if (!string.IsNullOrEmpty(cleanParagraph))
            {
                displayedParagraphs++;
                text.AppendLine();
                
                // Use different formatting for first paragraph (more emphasis)
                if (displayedParagraphs == 1)
                {
                    text.AppendLine($"<blockquote>{EscapeHtml(cleanParagraph)}</blockquote>");
                }
                else
                {
                    text.AppendLine(EscapeHtml(cleanParagraph));
                }
            }
        }
        
        if (paragraphs.Length > 3)
        {
            text.AppendLine();
            text.AppendLine("<i>📖 Content continues...</i>");
        }

        // Attachments section
        if (emailMessage.Attachments.Any())
        {
            text.AppendLine();
            text.AppendLine("<pre>═══════════════════════════════</pre>");
            text.AppendLine("<b>📎 Attachments:</b>");
            foreach (var attachment in emailMessage.Attachments)
            {
                text.AppendLine($"  <code>📁 {EscapeHtml(attachment.Filename)}</code> <i>({FormatFileSize(attachment.Size)})</i>");
            }
        }
        
        // Footer with Gmail link and read status
        text.AppendLine();
        text.AppendLine("<pre>═══════════════════════════════</pre>");
        
        // Show read status
        var readStatus = emailMessage.IsRead ? "✅ Read" : "🔵 Unread";
        text.AppendLine($"<b>Status:</b> <i>{readStatus}</i>");
        
        text.AppendLine($"<b>📬 <a href=\"{emailMessage.DirectLink}\">Open in Gmail</a></b>");
        
        return text.ToString();
    }

    private string BuildShortMessageText(EmailMessage emailMessage)
    {
        var text = new System.Text.StringBuilder();
        
        // Header with subject using blockquote for emphasis
        text.AppendLine($"<blockquote>📧 <b>{EscapeHtml(emailMessage.Subject)}</b></blockquote>");
        text.AppendLine();
        
        // Sender and date information
        text.AppendLine($"<b>👤 From:</b> <code>{EscapeHtml(emailMessage.Sender)}</code>");
        text.AppendLine($"<b>📅 Date:</b> <code>{emailMessage.ReceivedDateTime:yyyy-MM-dd HH:mm:ss} UTC</code>");
        
        // Labels if present
        if (emailMessage.Labels.Any()) 
        {
            text.AppendLine();
            text.AppendLine("<b>🏷️ Labels:</b>");
            var labelChunks = emailMessage.Labels.Select(l => $"<code>#{EscapeHtml(l.Replace(" ", "_"))}</code>").ToList();
            
            // Display labels in rows of 3
            for (int i = 0; i < labelChunks.Count; i += 3)
            {
                var chunk = labelChunks.Skip(i).Take(3);
                text.AppendLine($"  {string.Join(" ", chunk)}");
            }
        }
        
        // Message too long notice with better formatting
        text.AppendLine();
        text.AppendLine("<pre>═══════════════════════════════</pre>");
        text.AppendLine("<blockquote><b>⚠️ Message Too Long</b></blockquote>");
        text.AppendLine("<i>This email is too large to display in Telegram.</i>");
        text.AppendLine("Please use the link below to read the full content.");
        
        // Show read status
        text.AppendLine();
        var readStatus = emailMessage.IsRead ? "✅ Read" : "🔵 Unread";
        text.AppendLine($"<b>Status:</b> <i>{readStatus}</i>");
        
        text.AppendLine();
        text.AppendLine("<pre>═══════════════════════════════</pre>");
        text.AppendLine($"<b>📬 <a href=\"{emailMessage.DirectLink}\">Open in Gmail to read full message</a></b>");
        
        return text.ToString();
    }

    private string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // Remove script and style tags with their content
        html = System.Text.RegularExpressions.Regex.Replace(html, "<(script|style|head)[^>]*>.*?</\\1>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        // Convert some HTML tags to line breaks for better formatting
        html = System.Text.RegularExpressions.Regex.Replace(html, "<(br|BR)\\s*/?\\s*>", "\n", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "</(p|div|h[1-6]|li)\\s*>", "\n\n", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove all other HTML tags
        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");

        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);

        // Clean up excessive whitespace while preserving paragraph breaks
        html = System.Text.RegularExpressions.Regex.Replace(html, @"[ \t]+", " ");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\n[ \t]*\n", "\n\n");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\n{3,}", "\n\n");
        html = html.Trim();

        return html;
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


    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" }; double len = bytes; int order = 0; while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) { Console.WriteLine($"Telegram Bot Error: {exception.Message}"); return Task.CompletedTask; }
    /// <summary>
    /// Sends an error notification to the user via Telegram.
    /// </summary>
    /// <param name="errorMessage"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task NotifyErrorAsync(string errorMessage, CancellationToken cancellationToken)
    { if (_chatId != 0) { try { await _botClient.SendMessage(_chatId, $"⚠️ Error: {errorMessage}", cancellationToken: cancellationToken); } catch { } } }

    /// <summary>
    /// Sends a notification to the user via Telegram.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The cancellation token to stop the operation.</param>
    /// <returns></returns>
    public async Task NotifyAsync(string message, CancellationToken cancellationToken)
    { if (_chatId != 0) { try { await _botClient.SendMessage(_chatId, message, cancellationToken: cancellationToken); } catch { } } }

    /// <summary>
    /// Deletes a Telegram message.
    /// </summary>
    /// <param name="chatId">The chat ID of the message.</param>
    /// <param name="telegramMessageId">The Telegram message ID.</param>
    /// <param name="cancellationToken">The cancellation token to stop the operation.</param>
    /// <returns>True if deletion was successful, false otherwise.</returns>
    public async Task<bool> DeleteTelegramMessageAsync(long chatId, string telegramMessageId, CancellationToken cancellationToken)
    {
        try
        {
            if (int.TryParse(telegramMessageId, out int messageId))
            {
                await _botClient.DeleteMessage(chatId, messageId, cancellationToken);
                Console.WriteLine($"Deleted Telegram message {telegramMessageId} from chat {chatId}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting Telegram message {telegramMessageId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the chat ID.
    /// </summary>
    /// <returns>The chat ID.</returns>
    public long GetChatId() => _chatId;
    
    /// <summary>
    /// Sets the chat ID.
    /// </summary>
    /// <param name="chatId">The chat ID to set.</param>
    public void SetChatId(long chatId) => _chatId = chatId;
}
