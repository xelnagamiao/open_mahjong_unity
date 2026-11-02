using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Salasasa.Community {
    [Serializable] public sealed class CreditGroup { public string id, title, text; public int sort; }
    [Serializable] public sealed class CommunityAppearanceData { public string style = "original", palette = "slate"; }
    [Serializable] public sealed class CommunityEntry {
        public string id, kind, title, tag, number, description, joinMode, actionLabel, linkUrl, qrUrl, qrCaption, closedMessage, expiresAt;
        public string titleEn, descriptionEn, actionLabelEn, closedMessageEn;
        public bool qrIsExample;
        public int sort;
        public bool Expired => !string.IsNullOrEmpty(expiresAt) && DateTimeOffset.TryParse(expiresAt, out var time) && time <= DateTimeOffset.UtcNow;
        public bool Available => !Expired && joinMode != "closed";
        public string DisplayTitle => SelectText(title,titleEn);
        public string DisplayDescription => SelectText(description,descriptionEn);
        public string DisplayActionLabel => SelectText(actionLabel,actionLabelEn);
        public string DisplayClosedMessage => SelectText(closedMessage,closedMessageEn);
        static string SelectText(string original,string english) => ConfigManager.IsEnglish&&!string.IsNullOrWhiteSpace(english)
            ?english:ConfigManager.CurrentLanguage==AppLanguage.TraditionalChinese?PlatformText.Get(original):original??"";
        public static bool SafeLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort &&
            (uri.Host == "qm.qq.com" || uri.Host == "discord.gg" || uri.Host == "discord.com" || uri.Host == "t.me");
    }
    [Serializable] public sealed class PlatformContentDocument {
        public int schemaVersion, revision;
        public string updatedAt, thanksClosing;
        public CommunityAppearanceData appearance;
        public CreditGroup[] credits;
        public CommunityEntry[] communities;
        public string CreditsText => string.Join("\n\n", credits.Select(c => c.title + "：" + c.text)) + (string.IsNullOrEmpty(thanksClosing) ? "" : "\n\n" + thanksClosing);
    }
    [DisallowMultipleComponent]
    public sealed class PlatformContentClient : MonoBehaviour {
        public TextAsset bundledContent;
        public event Action Changed;
        public PlatformContentDocument Document { get; private set; }
        public string Status { get; private set; } = "";
        public bool Busy { get; private set; }
        public bool LastRequestSucceeded { get; private set; }
        public string Endpoint { get; private set; }
        string cachePath, etag;
        UnityWebRequest request;
        bool initialized, hasCache;

        public static PlatformContentDocument Parse(string json) {
            var d = JsonUtility.FromJson<PlatformContentDocument>(json);
            if (d == null || d.schemaVersion != 1 || d.revision < 1 || d.credits == null || d.communities == null || d.credits.Length > 24 || d.communities.Length > 48 || d.appearance == null)
                throw new FormatException("Invalid platform content document");
            if (d.credits.Any(x => x == null || string.IsNullOrWhiteSpace(x.title) || x.title.Length > 64 || x.text == null || x.text.Length > 5000) ||
                d.communities.Any(x => x == null || string.IsNullOrWhiteSpace(x.id) || string.IsNullOrWhiteSpace(x.title) || x.title.Length > 64 || x.description == null || x.description.Length > 480 || x.number == null || x.number.Length > 64 || x.actionLabel == null || x.actionLabel.Length > 16 ||
                    (x.titleEn?.Length??0)>96 || (x.descriptionEn?.Length??0)>960 || (x.actionLabelEn?.Length??0)>48 || (x.closedMessageEn?.Length??0)>128))
                throw new FormatException("Invalid platform content fields");
            if (d.communities.Select(x => x.id).Distinct().Count() != d.communities.Length) throw new FormatException("Duplicate community IDs");
            d.credits = d.credits.OrderBy(x => x.sort).ToArray(); d.communities = d.communities.OrderBy(x => x.sort).ToArray(); return d;
        }
        public void EnsureLoaded() {
            if (initialized) return;
            var developmentEndpoint = ConfigManager.Debug ? Salasasa.Home.CompleteLobbyEnvironment.Value("-community-api") : null;
            Endpoint = string.IsNullOrWhiteSpace(developmentEndpoint)
                ? ConfigManager.webApiUrl.TrimEnd('/') + "/api/platform/content"
                : developmentEndpoint;
            string key; using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Endpoint))).Replace("-", "").Substring(0, 16);
            cachePath = Path.Combine(Application.persistentDataPath, "platform-content-v2-" + key + ".json");
            Document = Parse(bundledContent.text); initialized = true;
            if (File.Exists(cachePath)) try { Document = Parse(File.ReadAllText(cachePath)); hasCache = true; Status = "已显示缓存内容"; } catch (Exception) { Status = ""; }
        }
        public void Refresh() { EnsureLoaded(); if (!Busy) StartCoroutine(Fetch()); }
        IEnumerator Fetch() {
            Busy = true; Status = "正在更新…"; Changed?.Invoke();
            using (var current = UnityWebRequest.Get(Endpoint)) {
                request = current; current.timeout = 8; current.redirectLimit = 0;
                if (!string.IsNullOrEmpty(etag)) current.SetRequestHeader("If-None-Match", etag);
                yield return current.SendWebRequest();
                LastRequestSucceeded = false;
                if (current.responseCode == 304) { LastRequestSucceeded = true; Status = ""; }
                else if (current.result == UnityWebRequest.Result.Success && current.downloadHandler.data.Length <= 262144) {
                    try {
                        var next = Parse(current.downloadHandler.text); Document = next; etag = current.GetResponseHeader("ETag"); hasCache = true; LastRequestSucceeded = true; Status = "";
                        try { File.WriteAllText(cachePath, current.downloadHandler.text); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Status = "内容已更新，无法保存缓存"; }
                    } catch (Exception) { Status = "内容暂不可用，已保留原内容"; }
                } else Status = hasCache ? "连接失败，已显示缓存内容" : "连接失败，已显示内置内容";
            }
            request = null; Busy = false; Changed?.Invoke();
        }
        public string ResolveQr(string relative) {
            if (string.IsNullOrEmpty(relative) || !System.Text.RegularExpressions.Regex.IsMatch(relative, @"^/api/platform/content/assets/([a-f0-9]{64}\.(png|jpg)|sample-wechat\.png)$")) return null;
            return new Uri(new Uri(Endpoint), relative).AbsoluteUri;
        }
        void OnDisable() { if (request != null) request.Abort(); StopAllCoroutines(); request = null; Busy = false; }
    }
}
