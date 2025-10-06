# OAuth Authentication Setup Guide

## Overview

The bot now uses **web-based OAuth authentication** through Telegram instead of hardcoded Google credentials. Each user authenticates their own Gmail account via the bot.

## Setup Steps

### 1. Create Google OAuth Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing one
3. Enable the **Gmail API**
4. Go to **APIs & Services > Credentials**
5. Click **Create Credentials > OAuth 2.0 Client ID**
6. Choose **Web application**
7. Add authorized redirect URI: `http://localhost:8080/oauth/callback`
8. Save your **Client ID** and **Client Secret**

### 2. Configure settings.json

Create `settings.json` from the template:

```json
{
  "telegram_bot_token": "YOUR_BOT_TOKEN_FROM_BOTFATHER",
  "polling_interval_seconds": 60,
  "database_path": "telegram_gmail.db",
  "gmail_scopes": [
    "https://www.googleapis.com/auth/gmail.readonly",
    "https://www.googleapis.com/auth/gmail.modify",
    "https://www.googleapis.com/auth/userinfo.email"
  ],
  "oauth_callback_url": "http://localhost:8080/oauth/callback",
  "oauth_callback_port": 8080,
  "application_name": "Telegram Gmail Bot"
}
```

**Note:** You no longer need to include `gmail_client_id` and `gmail_client_secret` in settings.json!

### 3. Start the Bot

```bash
dotnet run
```

The bot will:
- Start an OAuth callback HTTP server on port 8080
- Start the Telegram bot
- Wait for users to authenticate

## User Authentication Flow

### For End Users:

1. **Start the bot** in Telegram by sending `/start`

2. **Bot sends authentication link**: The bot will reply with a button "?? Connect Gmail Account"

3. **Click the link**: Opens Google OAuth consent screen in browser

4. **Grant permissions**: Allow the bot to access your Gmail

5. **Automatic redirect**: After approval, you're redirected back with a success message

6. **Done!** The bot will now forward your emails to Telegram

### Bot Commands:

- `/start` - Initialize and get OAuth link
- `/status` - Check authentication status
- `/disconnect` - Revoke access and delete stored tokens
- `/help` - Show available commands

## Security Features

? **Per-user authentication** - Each user authenticates their own account  
? **Token storage** - Access/refresh tokens stored securely in SQLite  
? **Token refresh** - Automatic token refresh when expired  
? **Revocation** - Users can disconnect anytime  
? **State validation** - CSRF protection with random state tokens  

## Database Schema

The bot creates these new tables:

### `user_credentials`
```sql
CREATE TABLE user_credentials (
    chat_id INTEGER PRIMARY KEY,
    access_token TEXT NOT NULL,
    refresh_token TEXT NOT NULL,
    expires_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    email_address TEXT
)
```

### `oauth_states`
```sql
CREATE TABLE oauth_states (
    state TEXT PRIMARY KEY,
    chat_id INTEGER NOT NULL,
    created_at DATETIME NOT NULL,
    expires_at DATETIME NOT NULL
)
```

## Architecture Changes

### Before (Old Flow):
```
User ? Bot Token in settings.json ? Single Gmail account for all users
```

### After (New Flow):
```
User ? Telegram Bot ? OAuth Link ? Google Consent ? User's Gmail Account
                    ?
              Token Storage (per user)
```

## Troubleshooting

### Port 8080 already in use
Change `oauth_callback_port` in settings.json and update your Google OAuth redirect URI accordingly.

### OAuth callback times out
- Ensure port 8080 is not blocked by firewall
- Check that redirect URI in Google Console matches exactly
- State tokens expire after 10 minutes - try again

### "Invalid grant" error
- Refresh token may have expired
- User needs to disconnect and re-authenticate
- Run `/disconnect` then `/start` again

## Production Deployment

For production with public domain:

1. Update `oauth_callback_url` to your domain:
   ```json
   "oauth_callback_url": "https://yourdomain.com/oauth/callback"
   ```

2. Add the URL to Google OAuth consent screen

3. Use a reverse proxy (nginx/Apache) to forward to the bot

4. Consider using HTTPS for the callback server

## Migration from Old Version

If you're upgrading from the credential-based version:

1. Backup your database: `cp telegram_gmail.db telegram_gmail.db.backup`
2. Update code and configuration
3. Existing users will need to re-authenticate via `/start`
4. Old `token.json` file is no longer used

## Support

For issues:
- Check logs in console output
- Verify OAuth credentials in Google Console
- Ensure all NuGet packages are restored: `dotnet restore`
- Build project: `dotnet build`

---

**Security Note**: Never commit `settings.json` or `telegram_gmail.db` to version control!
