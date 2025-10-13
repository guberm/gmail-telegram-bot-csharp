# Email Filter Feature

## Overview
The bot now supports filtering emails between **All Messages** and **Unread Only** modes. This filter preference is stored per user and persists across sessions.

## How to Use

### Method 1: Using the Filter Button (Recommended)
1. When you receive an email notification, you'll see a button row at the bottom:
   ```
   🔄 Filter: 📧 All  (or 📬 Unread)
   ```
2. Click this button to toggle between "All Messages" and "Unread Only"
3. The button updates immediately to show your current filter preference
4. When you use `/emails`, it will respect this setting

### Method 2: Using the /filter Command
1. Type `/filter` in the chat
2. The bot will toggle your filter preference and confirm the change
3. Example response:
   ```
   ✅ Filter updated!
   
   Current filter: 📬 Unread Only
   
   Use /emails to fetch emails with the new filter
   ```

### Method 3: Using the /emails Command
The `/emails` command now respects your filter preference:
```
/emails 10
```
- If filter is set to "All Messages": Fetches last 10 emails
- If filter is set to "Unread Only": Fetches last 10 unread emails

## Technical Implementation

### Database Changes
- **New Table**: `user_preferences`
  - `chat_id` (PRIMARY KEY): User's Telegram chat ID
  - `show_unread_only` (INTEGER): 0 = All Messages, 1 = Unread Only
  - `updated_at` (DATETIME): Last update timestamp

### Code Changes

#### 1. DatabaseEntities.cs
Added `UserPreference` entity:
```csharp
public class UserPreference
{
    public long ChatId { get; set; }
    public bool ShowUnreadOnly { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

#### 2. DatabaseService.cs
Added methods:
- `GetUserPreference(long chatId)`: Retrieves user's filter preference (defaults to "All Messages")
- `SetUserPreference(long chatId, bool showUnreadOnly)`: Updates user's filter preference

#### 3. GmailClient.cs
Updated methods to support filtering:
- `FetchInboxMessagesAsync(int maxResults = 10, bool unreadOnly = false)`: Added `unreadOnly` parameter
- `GetRecentEmailsAsync(int count = 5, bool unreadOnly = false)`: Added `unreadOnly` parameter
- When `unreadOnly = true`, adds Gmail query `is:unread` to fetch only unread messages

#### 4. TelegramBotService.cs
**New Command**: `/filter`
- Toggles filter preference
- Displays current filter status

**Updated /emails Command**:
- Reads user's filter preference
- Passes preference to Gmail API
- Shows filter status in messages (e.g., "Fetching your last 5 unread email(s)...")

**New Inline Keyboard Button**:
- Added "🔄 Filter" button to all email messages
- Shows current filter state: "📧 All" or "📬 Unread"
- Clicking toggles the preference and updates the button

**New Callback Handler**: `toggle_filter`
- Toggles user's filter preference in database
- Shows alert with new filter status
- Updates the button text immediately

## User Experience Flow

### Example 1: Switching to Unread Only
1. User receives email notification with buttons:
   ```
   ⚡ Actions  |  ⭐ Star
   🔄 Filter: 📧 All
   ```
2. User clicks "🔄 Filter: 📧 All"
3. Alert appears: "Filter updated: 📬 Unread Only"
4. Button updates to: "🔄 Filter: 📬 Unread"
5. User types `/emails 5`
6. Bot responds: "🔍 Fetching your last 5 unread email(s)..."
7. Only unread emails are displayed

### Example 2: Using /filter Command
1. User types: `/filter`
2. Bot responds:
   ```
   ✅ Filter updated!
   
   Current filter: 📬 Unread Only
   
   Use /emails to fetch emails with the new filter
   ```
3. Preference is now "Unread Only"

## Default Behavior
- New users default to **All Messages** (show everything)
- Filter preference is stored permanently until changed
- Each user has their own independent filter preference

## Benefits
1. **Reduce Noise**: Focus on unread emails only
2. **Quick Toggle**: Switch between modes with one click
3. **Persistent**: Preference saved across sessions
4. **Visual Feedback**: Button always shows current state
5. **Flexible**: Multiple ways to change the filter

## Commands Added/Updated
- **NEW**: `/filter` - Toggle between all emails and unread only
- **UPDATED**: `/emails` - Now respects filter preference
- **UPDATED**: `/help` - Includes documentation for /filter command

## Future Enhancements (Ideas)
- Add more filter options (starred, important, by label)
- Add date range filtering
- Add sender filtering
- Add search functionality with filters
