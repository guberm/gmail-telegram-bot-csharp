using Microsoft.Data.Sqlite;
using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

public partial class DatabaseService
{
    private void InitializeOAuthTables()
    {
        var createCredentialsTable = @"CREATE TABLE IF NOT EXISTS user_credentials (chat_id INTEGER PRIMARY KEY, access_token TEXT NOT NULL, refresh_token TEXT NOT NULL, expires_at DATETIME NOT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL, email_address TEXT)";
        var createOAuthStatesTable = @"CREATE TABLE IF NOT EXISTS oauth_states (state TEXT PRIMARY KEY, chat_id INTEGER NOT NULL, created_at DATETIME NOT NULL, expires_at DATETIME NOT NULL)";
        var createPaginationStatesTable = @"CREATE TABLE IF NOT EXISTS pagination_states (chat_id INTEGER PRIMARY KEY, current_page INTEGER NOT NULL, next_page_token TEXT, previous_page_token TEXT, has_more BOOLEAN NOT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL)";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = createCredentialsTable; cmd.ExecuteNonQuery();
        cmd.CommandText = createOAuthStatesTable; cmd.ExecuteNonQuery();
        cmd.CommandText = createPaginationStatesTable; cmd.ExecuteNonQuery();
    }
    
    /// <summary>
    /// Saves an OAuth state record to the database for tracking authorization requests.
    /// </summary>
    /// <param name="state">The OAuth state information to save.</param>
    public void SaveOAuthState(OAuthState state){var sql=@"INSERT OR REPLACE INTO oauth_states(state,chat_id,created_at,expires_at)VALUES(@state,@chat_id,@created_at,@expires_at)";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@state",state.State);cmd.Parameters.AddWithValue("@chat_id",state.ChatId);cmd.Parameters.AddWithValue("@created_at",state.CreatedAt.ToString("o"));cmd.Parameters.AddWithValue("@expires_at",state.ExpiresAt.ToString("o"));cmd.ExecuteNonQuery();}
    
    /// <summary>
    /// Retrieves an OAuth state record from the database if it exists and hasn't expired.
    /// </summary>
    /// <param name="state">The state identifier to look up.</param>
    /// <returns>The OAuth state information if found and valid, otherwise null.</returns>
    public OAuthState? GetOAuthState(string state){var sql="SELECT * FROM oauth_states WHERE state=@state AND expires_at>@now";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@state",state);cmd.Parameters.AddWithValue("@now",DateTime.UtcNow.ToString("o"));using var reader=cmd.ExecuteReader();if(reader.Read()){return new OAuthState{State=reader.GetString(0),ChatId=reader.GetInt64(1),CreatedAt=DateTime.Parse(reader.GetString(2)),ExpiresAt=DateTime.Parse(reader.GetString(3))};}return null;}
    
    /// <summary>
    /// Deletes an OAuth state record from the database.
    /// </summary>
    /// <param name="state">The state identifier to delete.</param>
    public void DeleteOAuthState(string state){var sql="DELETE FROM oauth_states WHERE state=@state";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@state",state);cmd.ExecuteNonQuery();}
    
    /// <summary>
    /// Saves user credentials (access tokens, refresh tokens) to the database.
    /// </summary>
    /// <param name="credentials">The user credentials to save.</param>
    public void SaveUserCredentials(UserCredentials credentials){var sql=@"INSERT OR REPLACE INTO user_credentials(chat_id,access_token,refresh_token,expires_at,created_at,updated_at,email_address)VALUES(@chat_id,@access_token,@refresh_token,@expires_at,@created_at,@updated_at,@email_address)";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@chat_id",credentials.ChatId);cmd.Parameters.AddWithValue("@access_token",credentials.AccessToken);cmd.Parameters.AddWithValue("@refresh_token",credentials.RefreshToken);cmd.Parameters.AddWithValue("@expires_at",credentials.ExpiresAt.ToString("o"));cmd.Parameters.AddWithValue("@created_at",credentials.CreatedAt.ToString("o"));cmd.Parameters.AddWithValue("@updated_at",credentials.UpdatedAt.ToString("o"));cmd.Parameters.AddWithValue("@email_address",credentials.EmailAddress??(object)DBNull.Value);cmd.ExecuteNonQuery();}
    
    /// <summary>
    /// Retrieves user credentials from the database by chat ID.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <returns>The user credentials if found, otherwise null.</returns>
    public UserCredentials? GetUserCredentials(long chatId){var sql="SELECT * FROM user_credentials WHERE chat_id=@chat_id";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@chat_id",chatId);using var reader=cmd.ExecuteReader();if(reader.Read()){return new UserCredentials{ChatId=reader.GetInt64(0),AccessToken=reader.GetString(1),RefreshToken=reader.GetString(2),ExpiresAt=DateTime.Parse(reader.GetString(3)),CreatedAt=DateTime.Parse(reader.GetString(4)),UpdatedAt=DateTime.Parse(reader.GetString(5)),EmailAddress=reader.IsDBNull(6)?string.Empty:reader.GetString(6)};}return null;}
    
    /// <summary>
    /// Checks whether a user has stored credentials in the database.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <returns>True if credentials exist for the user, otherwise false.</returns>
    public bool HasUserCredentials(long chatId){var sql="SELECT COUNT(*) FROM user_credentials WHERE chat_id=@chat_id";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@chat_id",chatId);var count=(long)(cmd.ExecuteScalar()??0L);return count>0;}
    
    /// <summary>
    /// Deletes user credentials from the database.
    /// </summary>
    /// <param name="chatId">The chat ID of the user whose credentials should be deleted.</param>
    public void DeleteUserCredentials(long chatId){var sql="DELETE FROM user_credentials WHERE chat_id=@chat_id";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@chat_id",chatId);cmd.ExecuteNonQuery();}
    
    /// <summary>
    /// Removes expired OAuth state records from the database.
    /// </summary>
    public void CleanupExpiredOAuthStates(){var sql="DELETE FROM oauth_states WHERE expires_at<@now";using var cmd=_connection.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("@now",DateTime.UtcNow.ToString("o"));cmd.ExecuteNonQuery();}

    // Pagination state management
    /// <summary>
    /// Saves pagination state information for a user's email browsing session.
    /// </summary>
    /// <param name="state">The pagination state to save.</param>
    public void SavePaginationState(PaginationState state)
    {
        var sql = @"INSERT OR REPLACE INTO pagination_states 
                   (chat_id, current_page, next_page_token, previous_page_token, has_more, created_at, updated_at) 
                   VALUES (@chat_id, @current_page, @next_page_token, @previous_page_token, @has_more, @created_at, @updated_at)";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chat_id", state.ChatId);
        cmd.Parameters.AddWithValue("@current_page", state.CurrentPage);
        cmd.Parameters.AddWithValue("@next_page_token", state.NextPageToken ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@previous_page_token", state.PreviousPageToken ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@has_more", state.HasMore);
        cmd.Parameters.AddWithValue("@created_at", state.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Retrieves saved pagination state for a user's email browsing session.
    /// </summary>
    /// <param name="chatId">The chat ID of the user.</param>
    /// <returns>The pagination state if found, otherwise null.</returns>
    public PaginationState? GetPaginationState(long chatId)
    {
        var sql = "SELECT * FROM pagination_states WHERE chat_id = @chat_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chat_id", chatId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new PaginationState
            {
                ChatId = reader.GetInt64(0),
                CurrentPage = reader.GetInt32(1),
                NextPageToken = reader.IsDBNull(2) ? null : reader.GetString(2),
                PreviousPageToken = reader.IsDBNull(3) ? null : reader.GetString(3),
                HasMore = reader.GetBoolean(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    /// <summary>
    /// Deletes pagination state information for a user.
    /// </summary>
    /// <param name="chatId">The chat ID of the user whose pagination state should be deleted.</param>
    public void DeletePaginationState(long chatId)
    {
        var sql = "DELETE FROM pagination_states WHERE chat_id = @chat_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chat_id", chatId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Retrieves all user credentials stored in the database.
    /// </summary>
    /// <returns>A list of all user credentials.</returns>
    public List<UserCredentials> GetAllUserCredentials()
    {
        var credentials = new List<UserCredentials>();
        var sql = "SELECT * FROM user_credentials";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            credentials.Add(new UserCredentials
            {
                ChatId = reader.GetInt64(0),
                AccessToken = reader.GetString(1),
                RefreshToken = reader.GetString(2),
                ExpiresAt = DateTime.Parse(reader.GetString(3)),
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                UpdatedAt = DateTime.Parse(reader.GetString(5)),
                EmailAddress = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }
        return credentials;
    }

    /// <summary>
    /// Removes expired or invalid user credentials from the database.
    /// </summary>
    /// <param name="chatId">The chat ID of the user whose expired credentials should be cleaned up.</param>
    public void CleanupExpiredCredentials(long chatId)
    {
        var sql = "DELETE FROM user_credentials WHERE chat_id = @chat_id";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chat_id", chatId);
        var rowsAffected = cmd.ExecuteNonQuery();
        if (rowsAffected > 0)
        {
            Console.WriteLine($"[DEBUG] Cleaned up expired credentials for chat {chatId}");
        }
    }
}