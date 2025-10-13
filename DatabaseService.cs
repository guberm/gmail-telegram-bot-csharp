using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

/// <summary>
/// Provides database operations for email messages, user actions, and OAuth-related data storage.
/// </summary>
public partial class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    
    /// <summary>
    /// Initializes a new instance of the DatabaseService with the specified database path.
    /// </summary>
    /// <param name="databasePath">The path to the SQLite database file.</param>
    public DatabaseService(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        InitializeDatabase();
    }
    private void InitializeDatabase()
    {
        var createMessagesTable = @"CREATE TABLE IF NOT EXISTS messages (message_id TEXT PRIMARY KEY, subject TEXT, sender TEXT, received_datetime DATETIME, content TEXT, attachments TEXT, labels TEXT, direct_link TEXT, is_read INTEGER, telegram_message_id TEXT)";
        var createActionsTable = @"CREATE TABLE IF NOT EXISTS actions (id INTEGER PRIMARY KEY AUTOINCREMENT, message_id TEXT, action_type TEXT, action_timestamp DATETIME, user_id TEXT, new_label_values TEXT, FOREIGN KEY (message_id) REFERENCES messages(message_id))";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = createMessagesTable; cmd.ExecuteNonQuery();
        cmd.CommandText = createActionsTable; cmd.ExecuteNonQuery();
        
        // Initialize OAuth tables
        InitializeOAuthTables();
    }
    
    /// <summary>
    /// Inserts a new email message or updates an existing one in the database.
    /// </summary>
    /// <param name="message">The email message to insert or update.</param>
    public void InsertOrUpdateMessage(EmailMessage message)
    {
        var sql = @"INSERT OR REPLACE INTO messages (message_id, subject, sender, received_datetime, content, attachments, labels, direct_link, is_read, telegram_message_id) VALUES (@message_id,@subject,@sender,@received_datetime,@content,@attachments,@labels,@direct_link,@is_read,@telegram_message_id)";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", message.MessageId);
        cmd.Parameters.AddWithValue("@subject", message.Subject);
        cmd.Parameters.AddWithValue("@sender", message.Sender);
        cmd.Parameters.AddWithValue("@received_datetime", message.ReceivedDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("@content", message.Content);
        cmd.Parameters.AddWithValue("@attachments", JsonConvert.SerializeObject(message.Attachments));
        cmd.Parameters.AddWithValue("@labels", JsonConvert.SerializeObject(message.Labels));
        cmd.Parameters.AddWithValue("@direct_link", message.DirectLink);
        cmd.Parameters.AddWithValue("@is_read", message.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@telegram_message_id", message.TelegramMessageId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    
    /// <summary>
    /// Retrieves an email message from the database by its message ID.
    /// </summary>
    /// <param name="messageId">The unique identifier of the email message.</param>
    /// <returns>The email message if found, otherwise null.</returns>
    public EmailMessage? GetMessage(string messageId)
    {
        var sql = "SELECT * FROM messages WHERE message_id = @message_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql; cmd.Parameters.AddWithValue("@message_id", messageId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new EmailMessage
            {
                MessageId = reader.GetString(0),
                Subject = reader.GetString(1),
                Sender = reader.GetString(2),
                ReceivedDateTime = DateTime.Parse(reader.GetString(3)),
                Content = reader.GetString(4),
                Attachments = JsonConvert.DeserializeObject<List<EmailAttachment>>(reader.GetString(5)) ?? new(),
                Labels = JsonConvert.DeserializeObject<List<string>>(reader.GetString(6)) ?? new(),
                DirectLink = reader.GetString(7),
                IsRead = reader.GetInt32(8) == 1,
                TelegramMessageId = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
        }
        return null;
    }
    
    /// <summary>
    /// Checks whether an email message exists in the database.
    /// </summary>
    /// <param name="messageId">The unique identifier of the email message.</param>
    /// <returns>True if the message exists, otherwise false.</returns>
    public bool MessageExists(string messageId)
    {
        var sql = "SELECT COUNT(*) FROM messages WHERE message_id = @message_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql; cmd.Parameters.AddWithValue("@message_id", messageId);
        var count = (long)(cmd.ExecuteScalar() ?? 0L); return count > 0;
    }
    
    /// <summary>
    /// Inserts a new message action record into the database.
    /// </summary>
    /// <param name="action">The message action to record.</param>
    public void InsertAction(MessageAction action)
    {
        var sql = @"INSERT INTO actions (message_id, action_type, action_timestamp, user_id, new_label_values) VALUES (@message_id,@action_type,@action_timestamp,@user_id,@new_label_values)";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", action.MessageId);
        cmd.Parameters.AddWithValue("@action_type", action.ActionType);
        cmd.Parameters.AddWithValue("@action_timestamp", action.ActionTimestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@user_id", action.UserId);
        cmd.Parameters.AddWithValue("@new_label_values", action.NewLabelValues != null ? JsonConvert.SerializeObject(action.NewLabelValues) : (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    
    /// <summary>
    /// Retrieves all actions performed on a specific email message.
    /// </summary>
    /// <param name="messageId">The unique identifier of the email message.</param>
    /// <returns>A list of message actions ordered by timestamp in descending order.</returns>
    public List<MessageAction> GetActionsForMessage(string messageId)
    {
        var sql = "SELECT * FROM actions WHERE message_id = @message_id ORDER BY action_timestamp DESC";
        var actions = new List<MessageAction>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql; cmd.Parameters.AddWithValue("@message_id", messageId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            actions.Add(new MessageAction
            {
                Id = reader.GetInt64(0),
                MessageId = reader.GetString(1),
                ActionType = reader.GetString(2),
                ActionTimestamp = DateTime.Parse(reader.GetString(3)),
                UserId = reader.GetString(4),
                NewLabelValues = reader.IsDBNull(5) ? null : JsonConvert.DeserializeObject<List<string>>(reader.GetString(5))
            });
        }
        return actions;
    }

    /// <summary>
    /// Retrieves all email messages associated with a specific user.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <returns>A list of email messages for the specified user.</returns>
    public List<EmailMessage> GetAllMessagesForUser(long chatId)
    {
        var sql = @"SELECT m.* FROM messages m 
                   JOIN user_credentials u ON 1=1 
                   WHERE u.chat_id = @chat_id";
        var messages = new List<EmailMessage>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chat_id", chatId);
        using var reader = cmd.ExecuteReader();
        
        while (reader.Read())
        {
            var message = new EmailMessage
            {
                MessageId = reader.GetString(0),
                Subject = reader.GetString(1),
                Sender = reader.GetString(2),
                ReceivedDateTime = DateTime.Parse(reader.GetString(3)),
                Content = reader.GetString(4),
                Attachments = JsonConvert.DeserializeObject<List<EmailAttachment>>(reader.GetString(5)) ?? new List<EmailAttachment>(),
                Labels = JsonConvert.DeserializeObject<List<string>>(reader.GetString(6)) ?? new List<string>(),
                DirectLink = reader.GetString(7),
                IsRead = reader.GetInt32(8) == 1,
                TelegramMessageId = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
            messages.Add(message);
        }
        return messages;
    }

    /// <summary>
    /// Deletes an email message from the database.
    /// </summary>
    /// <param name="messageId">The unique identifier of the email message to delete.</param>
    /// <returns>True if the message was successfully deleted, otherwise false.</returns>
    public bool DeleteMessage(string messageId)
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM messages WHERE message_id = @message_id";
            cmd.Parameters.AddWithValue("@message_id", messageId);
            var rowsAffected = cmd.ExecuteNonQuery();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting message {messageId}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Releases all resources used by the DatabaseService.
    /// </summary>
    public void Dispose() => _connection?.Dispose();
}
