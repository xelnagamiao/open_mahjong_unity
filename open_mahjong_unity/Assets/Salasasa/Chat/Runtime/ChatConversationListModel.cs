using System;
using System.Collections.Generic;
using System.Linq;

namespace Salasasa.Chat
{
    public static class ChatConversationListModel
    {
        public const string LobbyKey = "lobby";
        public const string RoomKey = "room";

        public static bool IsFixed(string key) => key == LobbyKey || key == RoomKey;
        public static string DirectKey(int first, int second) => "dm:" + Math.Min(first, second) + ":" + Math.Max(first, second);
        public static string VenueKey(string eventId) => "venue:" + eventId;
        public static bool Matches(string query, params string[] values)
        {
            string search = (query ?? "").Trim();
            return search.Length == 0 || values.Any(value => (value ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static ChatConversationInfo[] Conversations(IEnumerable<ChatConversationInfo> source, string query, ISet<string> pendingRemoval)
            => (source ?? Array.Empty<ChatConversationInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.key) && !IsFixed(item.key)
                    && (pendingRemoval == null || !pendingRemoval.Contains(item.key))
                    && Matches(query, item.title, item.last_message, item.peer_user_id > 0 ? item.peer_user_id.ToString() : item.event_id))
                .GroupBy(item => item.key, StringComparer.Ordinal).Select(group => group.Last())
                .Where(item => !string.IsNullOrWhiteSpace(item.last_message))
                .OrderByDescending(item => SortTime(item.last_message_at))
                .ThenBy(item => item.title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.key, StringComparer.Ordinal).ToArray();

        public static ChatVenueInfo[] Venues(IEnumerable<ChatVenueInfo> source, string query)
            => (source ?? Array.Empty<ChatVenueInfo>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.event_id) && Matches(query, item.name, item.event_id))
                .GroupBy(item => item.event_id, StringComparer.Ordinal).Select(group => group.Last())
                .OrderByDescending(item => item.can_send)
                .ThenBy(item => item.name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.event_id, StringComparer.Ordinal).ToArray();

        public static string TimeText(string value)
        {
            if (!DateTimeOffset.TryParse(value, out var time)) return "";
            var local = time.LocalDateTime;
            return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("MM/dd");
        }

        static long SortTime(string value) => DateTimeOffset.TryParse(value, out var time) ? time.UtcDateTime.Ticks : 0;
    }
}
