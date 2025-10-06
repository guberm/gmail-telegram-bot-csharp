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
}
