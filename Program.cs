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
            
            // Validate required settings
            if (string.IsNullOrEmpty(settings.TelegramBotToken))
            {
                Console.WriteLine("ERROR: Please configure settings.json with telegram_bot_token");
                return;
            }
            
            if (string.IsNullOrEmpty(settings.GoogleClientId) || string.IsNullOrEmpty(settings.GoogleClientSecret))
            {
                Console.WriteLine("ERROR: Please configure settings.json with google_client_id and google_client_secret");
                Console.WriteLine("See OAUTH_SETUP.md for detailed instructions.");
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
            var oauthService = new OAuthService(databaseService, settings);
            var oauthCallbackServer = new OAuthCallbackServer(databaseService, settings.OAuthCallbackPort);
            oauthCallbackServer.Start();
            Console.WriteLine($"OAuth callback server started on port {settings.OAuthCallbackPort}");
            Console.WriteLine($"OAuth server running on port {settings.OAuthCallbackPort}\n");
            
            Console.WriteLine("Starting Telegram bot...");
            var gmailClient = new GmailClient(settings);
            var telegramService = new TelegramBotService(settings.TelegramBotToken, gmailClient, databaseService, oauthService, settings);
            var emailPollingService = new EmailPollingService(gmailClient, telegramService, databaseService, settings);
            var cancellationTokenSource = new CancellationTokenSource();
            
            // Dictionary to track polling tasks for each user
            var pollingTasks = new Dictionary<long, Task>();
            
            // Setup OAuth callback handling
            oauthCallbackServer.CallbackReceived += async (sender, e) =>
            {
                Console.WriteLine($"OAuth callback received for chat {e.ChatId}");
                try
                {
                    var credentials = await oauthService.ExchangeCodeForTokensAsync(
                        e.Code, 
                        settings.GoogleClientId, 
                        settings.GoogleClientSecret, 
                        e.ChatId);
                        
                    if (credentials != null)
                    {
                        Console.WriteLine($"OAuth successful for {credentials.EmailAddress} (chat {e.ChatId})");
                        await telegramService.HandleOAuthSuccess(e.ChatId, credentials.EmailAddress, cancellationTokenSource.Token);
                        
                        // Authenticate Gmail client with user's tokens
                        var authSuccess = await gmailClient.AuthenticateAsync(credentials.AccessToken, credentials.RefreshToken);
                        if (!authSuccess)
                        {
                            Console.WriteLine($"Failed to authenticate Gmail client for chat {e.ChatId}");
                            return;
                        }
                        
                        // Set the current user for Gmail client
                        gmailClient.SetCurrentUser(e.ChatId);
                        
                        // Start email polling for this user
                        if (!pollingTasks.ContainsKey(e.ChatId))
                        {
                            Console.WriteLine($"Starting email polling for user {credentials.EmailAddress}");
                            var pollingTask = Task.Run(async () =>
                            {
                                try
                                {
                                    await emailPollingService.StartPollingAsync(e.ChatId, cancellationTokenSource.Token);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Email polling error for chat {e.ChatId}: {ex.Message}");
                                }
                            });
                            pollingTasks[e.ChatId] = pollingTask;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"OAuth failed for chat {e.ChatId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling OAuth callback for chat {e.ChatId}: {ex.Message}");
                }
            };
            
            await telegramService.StartAsync(cancellationTokenSource.Token);
            Console.WriteLine("Telegram bot started\n");
            
            // Start email polling for any existing authenticated users
            Console.WriteLine("Checking for existing authenticated users...");
            var existingUsers = databaseService.GetAllUserCredentials();
            Console.WriteLine($"Found {existingUsers.Count} existing authenticated users");
            
            foreach (var user in existingUsers)
            {
                if (!pollingTasks.ContainsKey(user.ChatId))
                {
                    Console.WriteLine($"Starting email polling for existing user {user.EmailAddress} (chat {user.ChatId})");
                    var pollingTask = Task.Run(async () =>
                    {
                        try
                        {
                            await emailPollingService.StartPollingAsync(user.ChatId, cancellationTokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Email polling error for chat {user.ChatId}: {ex.Message}");
                        }
                    });
                    pollingTasks[user.ChatId] = pollingTask;
                }
            }
            
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