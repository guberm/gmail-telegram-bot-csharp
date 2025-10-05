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
            if (string.IsNullOrEmpty(settings.TelegramBotToken))
            {
                Console.WriteLine("ERROR: Please configure settings.json with telegram_bot_token");
                return;
            }
            
            Console.WriteLine($"Polling interval: {settings.PollingIntervalSeconds} seconds");
            Console.WriteLine($"Database path: {settings.DatabasePath}");
            Console.WriteLine($"OAuth callback: {settings.OAuthCallbackUrl}");
            Console.WriteLine();
            
            Console.WriteLine("Initializing database...");
            using var databaseService = new DatabaseService(settings.DatabasePath);
            Console.WriteLine("Database initialized\n");
            
            Console.WriteLine("Starting OAuth callback server...");
            var oauthCallbackServer = new OAuthCallbackServer(databaseService, settings.OAuthCallbackPort);
            var oauthService = new OAuthService(databaseService, settings);
            oauthCallbackServer.Start();
            Console.WriteLine($"OAuth server running on port {settings.OAuthCallbackPort}\n");
            
            Console.WriteLine("Starting Telegram bot...");
            var gmailClient = new GmailClient(settings);
            var telegramService = new TelegramBotService(settings.TelegramBotToken, gmailClient, databaseService);
            var cancellationTokenSource = new CancellationTokenSource();
            
            // Setup OAuth callback handling
            oauthCallbackServer.CallbackReceived += async (sender, e) =>
            {
                Console.WriteLine($"OAuth callback received for chat {e.ChatId}");
                // Note: Token exchange will be handled by TelegramBotService when it detects the callback
            };
            
            await telegramService.StartAsync(cancellationTokenSource.Token);
            Console.WriteLine("Telegram bot started\n");
            
            Console.WriteLine("Bot is ready!");
            Console.WriteLine("Users can authenticate via Telegram by sending /start to the bot");
            Console.WriteLine("Press Ctrl+C to stop\n");
            
            Console.CancelKeyPress += (sender, e) => 
            { 
                e.Cancel = true; 
                cancellationTokenSource.Cancel();
                oauthCallbackServer.Stop();
            };
            
            await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nShutting down...");
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
