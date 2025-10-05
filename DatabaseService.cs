using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
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
    }
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
    public bool MessageExists(string messageId)
    {
        var sql = "SELECT COUNT(*) FROM messages WHERE message_id = @message_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql; cmd.Parameters.AddWithValue("@message_id", messageId);
        var count = (long)(cmd.ExecuteScalar() ?? 0L); return count > 0;
    }
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
    public void Dispose() => _connection?.Dispose();
}
