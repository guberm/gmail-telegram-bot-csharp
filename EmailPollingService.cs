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
        var retryCount = 0; const int maxRetries = 3;
        while (retryCount < maxRetries)
        {
            try
            {
                Console.WriteLine($"Fetching inbox messages... (Attempt {retryCount + 1}/{maxRetries})");
                var messages = await _gmailService.FetchInboxMessagesAsync(10);
                Console.WriteLine($"Found {messages.Count} messages in inbox");
                var newMessages = messages.Where(m => !_databaseService.MessageExists(m.MessageId)).ToList();
                if (newMessages.Any())
                {
                    Console.WriteLine($"Processing {newMessages.Count} new messages");
                    foreach (var message in newMessages)
                    {
                        try
                        {
                            _databaseService.InsertOrUpdateMessage(message);
                            await _telegramService.SendEmailAsync(message, chatId, cancellationToken);
                            var storedMessage = _databaseService.GetMessage(message.MessageId);
                            if (storedMessage == null || string.IsNullOrEmpty(storedMessage.TelegramMessageId))
                                throw new Exception($"Failed to verify message storage for {message.MessageId}");
                            _processedMessageIds.Add(message.MessageId);
                            Console.WriteLine($"Successfully processed: {message.Subject}");
                            await Task.Delay(500, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing message {message.MessageId}: {ex.Message}");
                            try { _databaseService.InsertAction(new MessageAction { MessageId = message.MessageId, ActionType = "error", ActionTimestamp = DateTime.UtcNow, UserId = "system", NewLabelValues = new List<string> { ex.Message } }); } catch { }
                        }
                    }
                }
                else { Console.WriteLine("No new messages"); }
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
