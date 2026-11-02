using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Salasasa.Venues
{
    public enum VenueKind { Base, Event }
    public enum VenuePhase { Upcoming, Running, Undated, Ended }
    public enum VenueSort { Recommended, CreatedNewest, CreatedOldest }

    [Serializable]
    public sealed class VenueEntry
    {
        public string event_id, name, description, status, kind;
        public string planned_start_at, planned_end_at, created_at;
        public int member_count = -1;
        public bool auto_approve, has_join_code, forbid_tourist, is_registered;
        public string min_rank, max_rank, create_room_permission;
        public string announcement;

        public VenueKind Kind => kind == "base" ? VenueKind.Base : VenueKind.Event;
        public DateTime? Start => ParseDate(planned_start_at);
        public DateTime? End => ParseDate(planned_end_at);
        public DateTime? Created => ParseDate(created_at);
        static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" };
        public static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var parsed)) return null;
            return parsed.Kind == DateTimeKind.Utc ? parsed.ToLocalTime() : parsed;
        }
        public static bool HasTime(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"(?:T|\s)\d{1,2}:\d{2}");
        public static string FormatDate(string value)
        {
            var date = ParseDate(value);
            return date.HasValue ? date.Value.ToString(HasTime(value) ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        }
        public string StartLabel => Start.HasValue ? "开赛时间：" + FormatDate(planned_start_at) : "日期待定";
        public static string BaseStatusLabel(string value) => value switch {
            "active" => "已开启", "registered" => "已注册", "closed" => "已关闭", "reopen_requested" => "重开审核中", _ => "—"
        };
        public VenuePhase Phase(DateTime now)
        {
            if (status == "closed" || (End.HasValue && End.Value < (HasTime(planned_end_at) ? now : now.Date))) return VenuePhase.Ended;
            if (Start.HasValue && Start.Value >= (HasTime(planned_start_at) ? now : now.Date)) return VenuePhase.Upcoming;
            if (status == "active") return VenuePhase.Running;
            return VenuePhase.Undated;
        }
        public string PhaseLabel(DateTime today)
        {
            if (Kind == VenueKind.Base) return BaseStatusLabel(status);
            switch (Phase(today))
            {
                case VenuePhase.Upcoming: return Start.HasValue && Start.Value.Date == today.Date ? "今日开赛" : "即将开赛";
                case VenuePhase.Running: return "进行中";
                case VenuePhase.Ended: return "已结束";
                default: return "待定";
            }
        }
        public string AccessLabel => has_join_code ? "凭加入码" : auto_approve ? "自由加入" : "人工审核";
        public string RankLabel => string.IsNullOrEmpty(min_rank) && string.IsNullOrEmpty(max_rank)
            ? "不限段位" : string.IsNullOrEmpty(max_rank) ? min_rank + "以上" : min_rank + " — " + max_rank;
    }

    public static class VenueQuery
    {
        public static string[] SortLabels(VenueKind kind) => new[] {
            kind == VenueKind.Base ? "成员人数：从多到少" : "最近开赛优先", "创建时间：最新优先", "创建时间：最早优先"
        };
        public static List<VenueEntry> Select(IEnumerable<VenueEntry> source, VenueKind kind, string search,
            int filter, DateTime today, VenueSort sort = VenueSort.Recommended)
        {
            var query = (search ?? "").Trim();
            var rows = (source ?? Enumerable.Empty<VenueEntry>()).Where(e => e != null && e.Kind == kind).Where(e =>
                query.Length == 0 || Contains(e.name, query) || Contains(e.description, query) || Contains(e.event_id, query));
            if (kind == VenueKind.Base)
            {
                if (filter == 1) rows = rows.Where(e => e.status == "active");
                if (filter == 2) rows = rows.Where(e => e.is_registered);
            }
            else
            {
                if (filter == 1) rows = rows.Where(e => e.Phase(today) == VenuePhase.Upcoming);
                if (filter == 2) rows = rows.Where(e => e.Phase(today) == VenuePhase.Running);
                if (filter == 3) rows = rows.Where(e => e.Phase(today) == VenuePhase.Ended);
            }
            if (sort == VenueSort.CreatedNewest)
                return rows.OrderBy(e => !e.Created.HasValue).ThenByDescending(e => e.Created)
                    .ThenBy(e => e.event_id, StringComparer.Ordinal).ToList();
            if (sort == VenueSort.CreatedOldest)
                return rows.OrderBy(e => !e.Created.HasValue).ThenBy(e => e.Created)
                    .ThenBy(e => e.event_id, StringComparer.Ordinal).ToList();
            if (kind == VenueKind.Base)
                return rows.OrderBy(e => e.member_count < 0).ThenByDescending(e => e.member_count)
                    .ThenBy(e => e.event_id, StringComparer.Ordinal).ToList();
            return rows.OrderBy(e => (int)e.Phase(today))
                .ThenBy(e => e.Start.HasValue ? (e.Phase(today) == VenuePhase.Upcoming
                    ? e.Start.Value.Ticks : -e.Start.Value.Ticks) : long.MaxValue)
                .ThenBy(e => e.event_id, StringComparer.Ordinal).ToList();
        }
        static bool Contains(string value, string query) => (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [Serializable]
    public sealed class VenueApplication
    {
        public string kind, name, description, planned_start_at, planned_end_at, organizer_name, organizer_phone, remark;
        public string Validate()
        {
            var label = kind == "base" ? "基地" : "赛事";
            if (string.IsNullOrWhiteSpace(name)) return "请填写" + label + "名称";
            if (name.Trim().Length > 128) return label + "名称最多 128 字";
            if (kind != "base" && string.IsNullOrWhiteSpace(planned_start_at)) return "请填写拟定开始时间";
            if (!string.IsNullOrWhiteSpace(planned_start_at) && !VenueEntry.ParseDate(planned_start_at).HasValue) return "拟定开始时间格式：YYYY-MM-DD";
            if (!string.IsNullOrWhiteSpace(planned_end_at) && !VenueEntry.ParseDate(planned_end_at).HasValue) return "拟定结束时间格式：YYYY-MM-DD";
            if (VenueEntry.ParseDate(planned_start_at).HasValue && VenueEntry.ParseDate(planned_end_at).HasValue &&
                VenueEntry.ParseDate(planned_end_at) < VenueEntry.ParseDate(planned_start_at)) return "拟定结束时间不能早于拟定开始时间";
            if (string.IsNullOrWhiteSpace(description)) return "请填写" + label + "介绍";
            if (description.Trim().Length > 2000) return label + "介绍最多 2000 字";
            if (string.IsNullOrWhiteSpace(organizer_name)) return "请填写负责人姓名";
            if (organizer_name.Trim().Length > 32) return "负责人姓名最多 32 字";
            var phone = Regex.Replace(organizer_phone ?? "", @"[\s-]", "");
            if (!Regex.IsMatch(phone, @"^1\d{10}$") && !Regex.IsMatch(phone, @"^\+?\d{7,15}$")) return "负责人手机号格式不正确";
            if ((remark ?? "").Trim().Length > 2000) return "备注最多 2000 字";
            return null;
        }
    }
}
