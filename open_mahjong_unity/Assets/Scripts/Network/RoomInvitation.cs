using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>A short room number is a locator; the full UUID identifies its lifetime.</summary>
public static class RoomInvitation {
    public sealed class Target {
        public readonly string RoomId, InstanceId;
        public Target(string roomId, string instanceId) { RoomId = roomId; InstanceId = instanceId; }
    }
    static readonly Regex RoomNumber = new Regex(@"\A[1-9][0-9]{0,9}\z", RegexOptions.CultureInvariant);
    public const string InvalidMessage = "邀请链接不完整或格式不正确，请重新复制完整的邀请链接。";

    public static bool TryIdentity(string roomId, string instanceId, out Target target) {
        target = null;
        if (string.IsNullOrEmpty(roomId) || !RoomNumber.IsMatch(roomId) || string.IsNullOrEmpty(instanceId)) return false;
        if (!Guid.TryParseExact(instanceId, "D", out Guid id) && !Guid.TryParseExact(instanceId, "N", out id)) return false;
        string canonical = id.ToString("D");
        if (canonical[14] != '4' || "89ab".IndexOf(canonical[19]) < 0) return false;
        target = new Target(roomId, canonical);
        return true;
    }

    public static bool TryParse(string url, out Target target) {
        target = null;
        if (string.IsNullOrEmpty(url) || url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.AbsolutePath.TrimEnd('/') != "/game-unity") return false;
        try {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in uri.Query.TrimStart('?').Split('&')) {
                if (part.Length == 0) continue;
                int separator = part.IndexOf('=');
                string key = Uri.UnescapeDataString(separator < 0 ? part : part.Substring(0, separator));
                string value = Uri.UnescapeDataString(separator < 0 ? "" : part.Substring(separator + 1));
                if (fields.ContainsKey(key)) return false;
                fields.Add(key, value);
            }
            if (fields.ContainsKey("recordId")) return false;
            return fields.TryGetValue("room", out var room) && fields.TryGetValue("instance", out var instance)
                && TryIdentity(room, instance, out target);
        } catch (UriFormatException) { return false; }
    }

    public static bool HasInviteParameters(string url) {
        return !string.IsNullOrEmpty(url) && Regex.IsMatch(url, @"[?&](room|instance)(?:=|&|#|$)", RegexOptions.CultureInvariant);
    }

    public static string BuildUrl(string webOrigin, string roomId, string instanceId) {
        if (!TryIdentity(roomId, instanceId, out var target)) return null;
        if (!Uri.TryCreate(webOrigin, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)) return null;
        return webOrigin.TrimEnd('/') + "/game-unity?room=" + target.RoomId + "&instance=" + target.InstanceId;
    }

    public static string RuleSummary(RoomInfo info) {
        string rule = RuleNameDictionary.GetWholeName(string.IsNullOrEmpty(info.sub_rule) ? info.room_rule : info.sub_rule);
        string length = info.unlimited_rounds ? PlatformText.Get("无固定时限")
            : PlatformText.Get(RoundTextDictionary.GetMaxRoundText(info.room_rule, info.game_round));
        return rule + " · " + length;
    }

    public static string ShareText(RoomInfo info, string origin) {
        string url = BuildUrl(origin, info.room_id, info.instance_id);
        return url == null ? null : PlatformText.Format("Salasasa 房间{0}（{1}）：", info.room_id, RuleSummary(info)) + "\n" + url;
    }
}
