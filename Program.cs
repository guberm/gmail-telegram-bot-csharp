using TelegramGmailBot.Models;
using TelegramGmailBot.Services;

namespace TelegramGmailBot;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Telegram Gmail Integration Bot ===");
        Console.WriteLine();
        try
        {
            Console.WriteLine("Loading configuration...");
            var settings = AppSettings.LoadFromFile("settings.json");
            if (string.IsNullOrEmpty(settings.GmailClientId) || string.IsNullOrEmpty(settings.GmailClientSecret) || string.IsNullOrEmpty(settings.TelegramBotToken))
            {
                Console.WriteLine("ERROR: Please configure settings.json with valid credentials");
                Console.WriteLine("Required fields:\n  - gmail_client_id\n  - gmail_client_secret\n  - telegram_bot_token");
                return;
            }
            Console.WriteLine($"Polling interval: {settings.PollingIntervalSeconds} seconds");
            Console.WriteLine($"Database path: {settings.DatabasePath}");
            Console.WriteLine();
            Console.WriteLine("Initializing database...");
            using var databaseService = new DatabaseService(settings.DatabasePath);
            Console.WriteLine("Database initialized\n");
            Console.WriteLine("Authenticating with Gmail...\nA browser window will open for authentication.");
            var gmailClient = new GmailClient(settings);
            var authenticated = await gmailClient.AuthenticateAsync();
            if (!authenticated)
            {
                Console.WriteLine("ERROR: Gmail authentication failed");
                return;
            }
            Console.WriteLine("Gmail authentication successful\n");
            Console.WriteLine("Starting Telegram bot...");
            var telegramService = new TelegramBotService(settings.TelegramBotToken, gmailClient, databaseService);
            var cancellationTokenSource = new CancellationTokenSource();
            await telegramService.StartAsync(cancellationTokenSource.Token);
            Console.WriteLine("Telegram bot started\n");
            Console.Write("Please enter your Telegram Chat ID (or press Enter to use test mode): ");
            var chatIdInput = Console.ReadLine();
            long chatId = 0;
            if (!string.IsNullOrEmpty(chatIdInput) && long.TryParse(chatIdInput, out var parsedChatId)) chatId = parsedChatId; else
            {
                Console.WriteLine("NOTE: Running in test mode. Send /start to the bot to begin receiving emails.");
                Console.WriteLine("To get your chat ID, message @userinfobot on Telegram.\n");
                Console.WriteLine("Waiting for user interaction...");
                await Task.Delay(10000);
                Console.WriteLine("WARNING: No chat ID provided. Emails will not be forwarded to Telegram.\nPress Ctrl+C to exit.\n");
            }
            if (chatId != 0)
            {
                Console.WriteLine($"Starting email polling for chat ID: {chatId}\nPress Ctrl+C to stop\n");
                var pollingService = new EmailPollingService(gmailClient, telegramService, databaseService, settings);
                Console.CancelKeyPress += (sender, e) => { e.Cancel = true; cancellationTokenSource.Cancel(); };
                await pollingService.StartPollingAsync(chatId, cancellationTokenSource.Token);
            }
            else
            {
                Console.WriteLine("Bot is running. Press Ctrl+C to exit.");
                await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.WriteLine();
        Console.WriteLine("Bot stopped. Press any key to exit.");
        Console.ReadKey();
    }
}
