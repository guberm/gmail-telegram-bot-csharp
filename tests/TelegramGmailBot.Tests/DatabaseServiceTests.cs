using System;
using System.IO;
using TelegramGmailBot.Models;
using TelegramGmailBot.Services;
using Xunit;

namespace TelegramGmailBot.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public void InsertAndRetrieveMessage_Works()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var service = new DatabaseService(dbPath);
            var message = new EmailMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Subject = "Test Subject",
                Sender = "sender@example.com",
                ReceivedDateTime = DateTime.UtcNow,
                Content = "Hello",
                DirectLink = "https://example.com",
                IsRead = false
            };
            service.InsertOrUpdateMessage(message);
            var loaded = service.GetMessage(message.MessageId);
            Assert.NotNull(loaded);
            Assert.Equal(message.Subject, loaded!.Subject);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
