# Telegram Gmail Integration Bot

[![.NET Build](https://github.com/guberm/gmail-telegram-bot-csharp/actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

A .NET 8 console application that authenticates with Gmail and forwards new inbox messages to a Telegram chat with inline action buttons (Delete, Archive, Star, Forward placeholder) and persistence via SQLite.

## Features
- Polls Gmail inbox and forwards new messages to Telegram
- Inline buttons for basic Gmail actions (delete/archive/star)
- Basic label display and modification (single label add)
- SQLite persistence for messages and actions
- Resilient polling with retries & exponential backoff
- Direct link to open each email in Gmail

## Quick Start
```bash
# clone
git clone git@github.com:guberm/gmail-telegram-bot-csharp.git
cd gmail-telegram-bot-csharp

# copy settings template
copy settings.json.template settings.json  # Windows
# Fill in credentials inside settings.json

# run
dotnet restore
dotnet run -c Release
```

When prompted, paste your Telegram chat ID (or press Enter to run in test mode and then send /start to your bot).

## Configuration (`settings.json`)
See `settings.json.template`. Never commit real credentials.

## Database
SQLite file (default: `telegram_gmail.db`) stores messages and action history.

## Running
- First run opens a browser for Gmail OAuth
- After auth, polling begins (interval from `polling_interval_seconds`)

## Roadmap
- Forward implementation
- Multiple account support
- Label management improvements
- Attachment downloads

## Contributing
See `CONTRIBUTING.md` for guidelines.

## License
MIT - see `LICENSE`.
