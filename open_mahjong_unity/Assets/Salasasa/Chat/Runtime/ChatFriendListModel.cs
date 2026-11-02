using System;
using System.Collections.Generic;
using System.Linq;

namespace Salasasa.Chat
{
    public static class ChatFriendListModel
    {
        public static int PresenceOrder(string state)
        {
            switch ((state ?? "").Trim().ToLowerInvariant())
            {
                case "online": return 0;
                case "in_game": case "in-game": case "ingame": return 1;
                default: return 2;
            }
        }

        public static string PresenceText(string state)
        {
            int order = PresenceOrder(state);
            return order == 0 ? "在线" : order == 1 ? "对局中" : "离线";
        }

        public static FriendInfo[] Filter(IEnumerable<FriendInfo> source, string query)
        {
            string search = (query ?? "").Trim();
            return (source ?? Array.Empty<FriendInfo>())
                .Where(f => f != null && f.user_id > 0 && (search.Length == 0
                    || (f.username ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || f.user_id.ToString().IndexOf(search, StringComparison.Ordinal) >= 0))
                .GroupBy(f => f.user_id).Select(g => g.Last())
                .OrderBy(f => PresenceOrder(f.state))
                .ThenBy(f => f.username ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.user_id).ToArray();
        }

        public static int WindowStart(int count, int poolSize, float rowHeight, float scrollY)
        {
            if (count <= poolSize || poolSize <= 0 || rowHeight <= 0) return 0;
            int first = Math.Max(0, (int)Math.Floor(Math.Max(0, scrollY) / rowHeight) - 1);
            return Math.Min(first, count - poolSize);
        }
    }
}
