# Telegram Gmail Integration Bot

[![.NET Build](https://github.com/guberm/gmail-telegram-bot-csharp/actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

A .NET 8 console application that integrates Gmail with Telegram using **OAuth 2.0 authentication**. Each user authenticates their own Gmail account through the bot, which then forwards new inbox messages to their Telegram chat with interactive action buttons.

## 🌟 Key Features

- 🔐 **OAuth 2.0 Authentication** - Per-user Gmail authentication via web flow
- 📨 **Email Forwarding** - Automatically forwards new inbox messages to Telegram
- 🎛️ **Interactive Buttons** - Delete, Archive, Star, and Forward actions
- 🔄 **Smart Filtering** - Toggle between all emails or unread only with one click
- 🏷️ **Label Management** - View and modify Gmail labels
- 💾 **SQLite Persistence** - Stores messages, actions, and user credentials
- 🔄 **Automatic Token Refresh** - Handles expired access tokens seamlessly
- 🔗 **Direct Links** - Quick access to open emails in Gmail
- 📊 **Action History** - Tracks all user actions on messages
- 🛡️ **CSRF Protection** - Secure OAuth state validation
- ⚡ **Resilient Polling** - Retries with exponential backoff

## 🚀 Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Telegram Bot Token (from [@BotFather](https://t.me/botfather))
- Google Cloud Project with Gmail API enabled

### Installation

```bash
# Clone the repository
git clone https://github.com/guberm/gmail-telegram-bot-csharp.git
cd gmail-telegram-bot-csharp

# Restore dependencies
dotnet restore

# Copy settings template
cp settings.json.template settings.json  # Linux/Mac
copy settings.json.template settings.json  # Windows
```

### Configuration

1. **Create Google OAuth Credentials**
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Enable Gmail API
   - Create OAuth 2.0 Client ID (Web application)
   - Add redirect URI: `http://localhost:8080/oauth/callback`
   - Copy the Client ID and Client Secret

2. **Get Telegram Bot Token**
   - Message [@BotFather](https://t.me/botfather) on Telegram
   - Create a new bot with `/newbot`
   - Copy the bot token

3. **Update `settings.json`**

```json
{
  "telegram_bot_token": "YOUR_BOT_TOKEN_FROM_BOTFATHER",
  "google_client_id": "YOUR_GOOGLE_CLIENT_ID",
  "google_client_secret": "YOUR_GOOGLE_CLIENT_SECRET",
  "polling_interval_seconds": 60,
  "database_path": "telegram_gmail.db",
  "gmail_scopes": [
    "https://www.googleapis.com/auth/gmail.readonly",
    "https://www.googleapis.com/auth/gmail.modify"
  ],
  "oauth_callback_url": "http://localhost:8080/oauth/callback",
  "oauth_callback_port": 8080,
  "application_name": "Telegram Gmail Bot"
}
```

**Important:** 
- ⚠️ Never commit `settings.json` to version control
- 🔐 Each user authenticates individually - no shared credentials
- 📚 See [OAUTH_SETUP.md](OAUTH_SETUP.md) for detailed setup instructions

### Running the Bot

```bash
dotnet run
```

The bot will:
1. ✅ Validate configuration (Telegram token + Google OAuth credentials)
2. 🗄️ Initialize SQLite database
3. 🌐 Start OAuth callback server on port 8080
4. 🤖 Start Telegram bot
5. ⏳ Wait for users to authenticate via `/start` command

## 🎯 User Guide

### Authentication Flow

1. **Start the bot** - Send `/start` to your bot on Telegram
2. **Click the OAuth link** - Bot responds with "🔐 Connect Gmail Account" button
3. **Authorize on Google** - Grant Gmail permissions in your browser
4. **Success!** - You'll be redirected and the bot confirms connection
5. **Receive emails** - New Gmail messages appear in your Telegram chat

### Available Commands

| Command | Description |
|---------|-------------|
| `/start` | Connect your Gmail account via OAuth authentication |
| `/status` | Check your Gmail connection status and details |
| `/emails [count]` | Fetch recent emails (default: 5, max: 20) |
| `/filter` | Toggle between showing all emails or unread only |
| `/disconnect` | Revoke access and delete stored credentials |
| `/help` | Show available commands and detailed usage guide |

### Email Actions

Each forwarded email includes inline buttons organized in a hierarchical menu:

**Main Menu:**
- ⚡ **Actions** - Opens submenu with email management options
- ⭐ **Star** - Add star to email
- � **Filter** - Toggle between "All Messages" or "Unread Only" mode

**Actions Submenu:**
- �🗑️ **Delete** - Move email to trash
- 📦 **Archive** - Remove from inbox (keep in All Mail)
- ➡️ **Forward** - Forward email to another address (coming soon)
- ✉️ **Mark as Read** - Mark email as read in Gmail
- ⬅️ **Back** - Return to main menu

### Email Filtering

The bot supports filtering emails to show only what you need:

- **All Messages** (📧) - Show all inbox emails
- **Unread Only** (📬) - Show only unread emails

**How to use:**
1. Click the `🔄 Filter` button on any email message
2. Or use the `/filter` command to toggle
3. Your preference is saved and applies to all `/emails` commands

**When filter is set to "Unread Only":**
- `/emails` fetches only unread messages
- The filter button shows: `🔄 Filter: 📬 Unread`
- **Auto-removal**: Emails automatically disappear from Telegram when marked as read (in bot, Gmail web, or mobile app)
- Synchronization happens during next polling cycle (~60 seconds)

**When filter is set to "All Messages":**
- All inbox emails are shown
- Marking emails as read does NOT remove them from Telegram
- Emails stay until manually deleted or archived

> ⚠️ **Important**: In "Unread Only" mode, marking an email as read will automatically remove it from your Telegram chat. This keeps your chat clean and focused on unread emails only.

See [FILTER_FEATURE.md](FILTER_FEATURE.md) for detailed documentation.

## 🏗️ Architecture

```
┌───────────┐     OAuth Link      ┌──────────────┐
│ Telegram  │◄───────────────────►│ Google OAuth │
│   Bot     │                     │   Server     │
└───────────┘                     └──────────────┘
       │                                   │
       │ Token Callback                    │ Auth Code
       │◄──────────────────────────────────┘
       │
       │ Store Credentials (SQLite)
       │
       │ Fetch Emails (Gmail API)
       │
       │ Forward to User (Telegram)
```

### Database Schema

#### `user_credentials`
Stores OAuth tokens per user
```sql
chat_id, access_token, refresh_token, expires_at, created_at, updated_at, email_address
```

#### `oauth_states`
CSRF protection for OAuth flow
```sql
state, chat_id, created_at, expires_at
```

#### `messages`
Email message cache
```sql
message_id, subject, sender, received_datetime, content, attachments, labels, direct_link, is_read, telegram_message_id
```

#### `actions`
User action history
```sql
id, message_id, action_type, action_timestamp, user_id, new_label_values
```

#### `user_preferences`
Per-user filter preferences
```sql
chat_id, show_unread_only, updated_at
```

## 🔒 Security Features

- 👤 **Per-user OAuth** - No shared credentials
- 🛡️ **CSRF Protection** - Random state validation
- 🔐 **Token Encryption** - Secure storage in SQLite
- 🔄 **Automatic Refresh** - Expired tokens handled transparently
- 🚫 **User Revocation** - Users can disconnect anytime
- ⏰ **State Expiration** - OAuth states expire after 10 minutes

## 📚 Documentation

- **[OAuth Setup Guide](OAUTH_SETUP.md)** - Detailed OAuth configuration and troubleshooting
- **[Filter Feature Guide](FILTER_FEATURE.md)** - Email filtering functionality and usage
- **[Contributing Guidelines](CONTRIBUTING.md)** - How to contribute to the project
- **[License](LICENSE)** - MIT License terms

## 🛠️ Development

### Building

```bash
dotnet build -c Release
```

### Running Tests

```bash
dotnet test
```

### Project Structure

```
gmail-telegram-bot-csharp/
├── Models/              # Data models
│   ├── AppSettings.cs
│   ├── UserCredentials.cs
│   └── OAuthState.cs
├── Services/            # Business logic
│   ├── OAuthService.cs
│   ├── OAuthCallbackServer.cs
│   ├── GmailClient.cs
│   └── TelegramBotService.cs
├── DatabaseService.cs   # SQLite persistence
├── Program.cs           # Entry point
└── tests/               # Unit tests
```

## 🗺️ Roadmap

- [x] **Email Filtering** - Toggle between all emails and unread only ✅
- [x] **Hierarchical Button Menu** - Organized action buttons with submenus ✅
- [ ] **Complete Forward implementation** - Send emails to other addresses
- [ ] **Multi-account support** - Multiple Gmail accounts per user
- [ ] **Enhanced filtering** - Filter by label, date range, sender
- [ ] **Enhanced label management** - Advanced label operations
- [ ] **Attachment handling** - Download and send attachments via Telegram
- [ ] **Search functionality** - Search emails via bot commands
- [ ] **Email composition** - Send new emails from Telegram
- [ ] **Custom Rules** - Advanced email filtering and automation rules
- [ ] **Notification customization** - Per-user notification preferences
- [ ] **Web dashboard** - Optional web interface for management

## 🤝 Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for:

- Code style guidelines
- Pull request process
- Development setup
- Testing requirements

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [.NET 8](https://dotnet.microsoft.com/)
- Uses [Google Gmail API](https://developers.google.com/gmail/api)
- Powered by [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot)
- Data persistence with [SQLite](https://www.sqlite.org/)

## 💬 Support

- **Issues**: [GitHub Issues](https://github.com/guberm/gmail-telegram-bot-csharp/issues)
- **Discussions**: [GitHub Discussions](https://github.com/guberm/gmail-telegram-bot-csharp/discussions)

---

⚠️ **Security Note**: Never commit `settings.json`, `telegram_gmail.db`, or any files containing credentials to version control!