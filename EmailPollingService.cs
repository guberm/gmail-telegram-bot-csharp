using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

public class EmailPollingService
{
    private readonly GmailClient _gmailService;
    private readonly TelegramBotService _telegramService;
    private readonly DatabaseService _databaseService;
    private readonly AppSettings _settings;
    private readonly HashSet<string> _processedMessageIds = new();

    public EmailPollingService(
        GmailClient gmailService,
        TelegramBotService telegramService,
        DatabaseService databaseService,
        AppSettings settings)
    {
        _gmailService = gmailService;
        _telegramService = telegramService;
        _databaseService = databaseService;
        _settings = settings;
    }

    public async Task StartPollingAsync(long chatId, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Starting email polling with interval: {_settings.PollingIntervalSeconds} seconds");
        _telegramService.SetChatId(chatId);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollEmailsAsync(chatId, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), cancellationToken);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in polling loop: {ex.Message}");
                try { await _telegramService.NotifyErrorAsync("Failed to fetch emails. Will retry...", cancellationToken); } catch { }
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        Console.WriteLine("Email polling stopped");
    }

    private async Task PollEmailsAsync(long chatId, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[DEBUG] Starting PollEmailsAsync for chat {chatId}");
        
        // Authenticate Gmail client for this user
        var credentials = _databaseService.GetUserCredentials(chatId);
        if (credentials == null)
        {
            Console.WriteLine($"[DEBUG] No credentials found for chat {chatId}");
            return;
        }
        
        var authSuccess = await _gmailService.AuthenticateAsync(credentials.AccessToken, credentials.RefreshToken);
        if (!authSuccess)
        {
            Console.WriteLine($"[DEBUG] Failed to authenticate Gmail client for chat {chatId}");
            return;
        }
        
        _gmailService.SetCurrentUser(chatId);
        Console.WriteLine($"[DEBUG] Gmail client authenticated for user {credentials.EmailAddress}");

        // First: Check for email synchronization (deleted emails)
        await SynchronizeDeletedEmailsAsync(chatId, cancellationToken);
        
        var retryCount = 0; const int maxRetries = 3;
        while (retryCount < maxRetries)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Fetching inbox messages... (Attempt {retryCount + 1}/{maxRetries})");
                var messages = await _gmailService.FetchInboxMessagesAsync(10);
                Console.WriteLine($"[DEBUG] Found {messages.Count} messages in inbox");
                
                // Log each message found
                for (int i = 0; i < messages.Count; i++)
                {
                    var msg = messages[i];
                    Console.WriteLine($"[DEBUG] Message {i + 1}: ID={msg.MessageId}, Subject='{msg.Subject}', From='{msg.Sender}', Date={msg.ReceivedDateTime:yyyy-MM-dd HH:mm:ss}");
                }
                
                var newMessages = messages.Where(m => !_databaseService.MessageExists(m.MessageId)).ToList();
                Console.WriteLine($"[DEBUG] {newMessages.Count} messages are new (not in database)");
                
                if (newMessages.Any())
                {
                    Console.WriteLine($"[DEBUG] Processing {newMessages.Count} new messages");
                    foreach (var message in newMessages)
                    {
                        try
                        {
                            Console.WriteLine($"[DEBUG] Processing message: {message.Subject} from {message.Sender}");
                            _databaseService.InsertOrUpdateMessage(message);
                            Console.WriteLine($"[DEBUG] Message saved to database");
                            
                            await _telegramService.SendEmailAsync(message, chatId, cancellationToken);
                            Console.WriteLine($"[DEBUG] Message sent to Telegram");
                            
                            var storedMessage = _databaseService.GetMessage(message.MessageId);
                            if (storedMessage == null || string.IsNullOrEmpty(storedMessage.TelegramMessageId))
                                throw new Exception($"Failed to verify message storage for {message.MessageId}");
                            _processedMessageIds.Add(message.MessageId);
                            Console.WriteLine($"[DEBUG] Successfully processed: {message.Subject}");
                            await Task.Delay(500, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] Error processing message {message.MessageId}: {ex.Message}");
                            Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                            try { _databaseService.InsertAction(new MessageAction { MessageId = message.MessageId, ActionType = "error", ActionTimestamp = DateTime.UtcNow, UserId = "system", NewLabelValues = new List<string> { ex.Message } }); } catch { }
                        }
                    }
                }
                else 
                { 
                    Console.WriteLine($"[DEBUG] No new messages found");
                    
                    // Show most recent message for debugging
                    if (messages.Any())
                    {
                        var latest = messages.First();
                        Console.WriteLine($"[DEBUG] Most recent message: '{latest.Subject}' from {latest.Sender} at {latest.ReceivedDateTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"[DEBUG] Message exists in DB: {_databaseService.MessageExists(latest.MessageId)}");
                    }
                }
                break; // success
            }
            catch (Exception ex)
            {
                retryCount++;
                Console.WriteLine($"Error fetching messages (attempt {retryCount}/{maxRetries}): {ex.Message}");
                if (retryCount >= maxRetries) throw;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                Console.WriteLine($"Retrying in {delay.TotalSeconds} seconds...");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task SynchronizeDeletedEmailsAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"[SYNC] Starting email synchronization for chat {chatId}");
            
            // Get all stored messages for this user from database
            var allStoredMessages = _databaseService.GetAllMessagesForUser(chatId);
            if (!allStoredMessages.Any())
            {
                Console.WriteLine($"[SYNC] No stored messages found for chat {chatId}");
                return;
            }

            // Only check recent messages (last 20) to avoid checking very old emails
            var recentStoredMessages = allStoredMessages
                .OrderByDescending(m => m.ReceivedDateTime)
                .Take(20)
                .ToList();

            Console.WriteLine($"[SYNC] Found {allStoredMessages.Count} stored messages total, checking {recentStoredMessages.Count} recent messages in Gmail...");

            var deletedCount = 0;
            Console.WriteLine($"[SYNC] Starting to check {recentStoredMessages.Count} messages...");
            
            foreach (var storedMessage in recentStoredMessages)
            {
                try
                {
                    Console.WriteLine($"[SYNC-DEBUG] Checking message {storedMessage.MessageId} (Subject: '{storedMessage.Subject}')");
                    
                    // Check if message still exists in Gmail INBOX
                    var stillInInbox = await _gmailService.MessageStillInInboxAsync(storedMessage.MessageId);
                    
                    Console.WriteLine($"[SYNC-DEBUG] Message {storedMessage.MessageId} still in INBOX: {stillInInbox}");
                    
                    if (!stillInInbox)
                    {
                        Console.WriteLine($"[SYNC] Message {storedMessage.MessageId} no longer in INBOX, deleting from Telegram...");
                        
                        // Delete from Telegram if we have the Telegram message ID
                        if (!string.IsNullOrEmpty(storedMessage.TelegramMessageId))
                        {
                            var deleteSuccess = await _telegramService.DeleteTelegramMessageAsync(chatId, storedMessage.TelegramMessageId, cancellationToken);
                            if (deleteSuccess)
                            {
                                Console.WriteLine($"[SYNC] Successfully deleted Telegram message {storedMessage.TelegramMessageId}");
                            }
                            else
                            {
                                Console.WriteLine($"[SYNC] Failed to delete Telegram message {storedMessage.TelegramMessageId} (message may already be deleted)");
                            }
                        }
                        
                        // Remove from database (always try to clean up, even if Telegram deletion failed)
                        try
                        {
                            var dbDeleteSuccess = _databaseService.DeleteMessage(storedMessage.MessageId);
                            if (dbDeleteSuccess)
                            {
                                deletedCount++;
                                Console.WriteLine($"[SYNC] Removed message '{storedMessage.Subject}' from database. Deleted count is now: {deletedCount}");
                            }
                            else
                            {
                                Console.WriteLine($"[SYNC] Failed to delete message '{storedMessage.Subject}' from database");
                            }
                        }
                        catch (Exception dbEx)
                        {
                            Console.WriteLine($"[SYNC] Database deletion error for message {storedMessage.MessageId}: {dbEx.Message}");
                            // Don't increment deletedCount for failed database deletions
                        }
                        
                        // Small delay to avoid hitting rate limits
                        await Task.Delay(200, cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine($"[SYNC-DEBUG] Message {storedMessage.MessageId} is still in INBOX - no action needed");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SYNC] Error checking message {storedMessage.MessageId}: {ex.Message}");
                    // Continue with other messages
                }
            }

            Console.WriteLine($"[SYNC] Finished checking all messages. Total deleted count: {deletedCount}");

            if (deletedCount > 0)
            {
                Console.WriteLine($"[SYNC] Synchronized {deletedCount} deleted emails for chat {chatId}");
                
                // Notify user about synchronization
                try
                {
                    var notificationText = deletedCount == 1 
                        ? "✅ Synced: 1 deleted email removed from chat"
                        : $"✅ Synced: {deletedCount} deleted emails removed from chat";
                    
                    Console.WriteLine($"[SYNC] Sending notification: {notificationText}");
                    await _telegramService.NotifyAsync(notificationText, cancellationToken);
                    Console.WriteLine($"[SYNC] Notification sent successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SYNC] Failed to notify user about synchronization: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[SYNC] No deleted emails found for chat {chatId} - no notification will be sent");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC] Error during email synchronization for chat {chatId}: {ex.Message}");
        }
    }
}
