using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class ActivityIndexFile {
    public string updated_at;
    public ActivityIndexItem[] items;
}

[Serializable]
public class ActivityIndexItem {
    public string id;
    public string title;
    public string cover_url;
    public string updated_at;
    public int sort;
    public string status;
    public bool ended;
}

[Serializable]
public class ActivityBodyImage {
    public string url;
    public string href;
    public string size;
}

[Serializable]
public class ActivityContentBlock {
    public string type;
    public string text;
    public int fontSize;
    public string size;
    public string url;
    public string href;
}

[Serializable]
public class ActivityDetail {
    public string id;
    public string title;
    public string body;
    public string cover_url;
    public ActivityContentBlock[] blocks;
    public ActivityBodyImage[] images;
    public string[] image_urls;
    public string status;
    public bool published;
    public bool ended;
    public int sort;
    public string created_at;
    public string updated_at;
}

public static class ActivityStatus {
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Ended = "ended";
    public const string Offline = "offline";

    public static bool IsEnded(string status, bool endedFlag) {
        return endedFlag || string.Equals(status, Ended, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsClientVisible(string status, bool endedFlag) {
        if (string.IsNullOrEmpty(status)) return true;
        if (IsEnded(status, endedFlag)) return true;
        return string.Equals(status, Published, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 活动专栏走 HTTPS 静态文件，不经过游戏 WebSocket。
/// </summary>
public static class ActivityHttp {
    public const string IndexPath = "/activity-assets/index.json";
    public const string PlatformListPath = "/api/platform/activities";

    [Serializable]
    private class PlatformActivitiesResponse {
        public bool success;
        public ActivityIndexFile data;
    }

    public static string ResolveUrl(string pathOrUrl) {
        if (string.IsNullOrEmpty(pathOrUrl)) return pathOrUrl;
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            return pathOrUrl;
        }
        string path = pathOrUrl.StartsWith("/") ? pathOrUrl : "/" + pathOrUrl;
        string root = ConfigManager.webApiUrl;
        if (string.IsNullOrEmpty(root)) return path;
        return root.TrimEnd('/') + path;
    }

    public static ActivityContentBlock[] BlocksOf(ActivityDetail detail) {
        if (detail == null) return new ActivityContentBlock[0];
        if (detail.blocks != null && detail.blocks.Length > 0) return detail.blocks;
        var list = new List<ActivityContentBlock>();
        if (!string.IsNullOrEmpty(detail.body)) {
            list.Add(new ActivityContentBlock { type = "text", text = detail.body, fontSize = 22 });
        }
        ActivityBodyImage[] images = BodyImagesOf(detail);
        if (images != null) {
            for (int i = 0; i < images.Length; i++) {
                if (images[i] == null || string.IsNullOrEmpty(images[i].url)) continue;
                list.Add(new ActivityContentBlock {
                    type = "image",
                    size = string.IsNullOrEmpty(images[i].size) ? "large" : images[i].size,
                    url = images[i].url,
                    href = images[i].href
                });
            }
        }
        return list.ToArray();
    }

    public static ActivityBodyImage[] BodyImagesOf(ActivityDetail detail) {
        if (detail == null) return null;
        if (detail.images != null && detail.images.Length > 0) return detail.images;
        if (detail.image_urls == null || detail.image_urls.Length == 0) return null;
        ActivityBodyImage[] mapped = new ActivityBodyImage[detail.image_urls.Length];
        for (int i = 0; i < detail.image_urls.Length; i++) {
            mapped[i] = new ActivityBodyImage { url = detail.image_urls[i], href = "" };
        }
        return mapped;
    }

    public static void OpenHref(string href) {
        if (string.IsNullOrEmpty(href)) return;
        string trimmed = href.Trim();
        string lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("vbscript:")) {
            return;
        }
        if (trimmed.StartsWith("/")) {
            trimmed = ResolveUrl(trimmed);
        } else if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        Application.OpenURL(trimmed);
    }

    public static bool IsSafeHref(string href) {
        if (string.IsNullOrEmpty(href)) return false;
        string trimmed = href.Trim();
        string lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("vbscript:")) {
            return false;
        }
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/");
    }

    public static string ToTmpRichText(string text) {
        if (string.IsNullOrEmpty(text)) return "";
        var re = new System.Text.RegularExpressions.Regex(@"\[([^\]]+)\]\(([^)]+)\)");
        var sb = new System.Text.StringBuilder();
        int last = 0;
        foreach (System.Text.RegularExpressions.Match match in re.Matches(text)) {
            sb.Append(EscapeTmp(text.Substring(last, match.Index - last)));
            string label = EscapeTmp(match.Groups[1].Value);
            string href = match.Groups[2].Value.Trim();
            if (IsSafeHref(href) && href.IndexOf('"') < 0) {
                sb.Append("<link=\"").Append(href).Append("\"><color=#7EC8FF><u>");
                sb.Append(label);
                sb.Append("</u></color></link>");
            } else {
                sb.Append(label);
            }
            last = match.Index + match.Length;
        }
        sb.Append(EscapeTmp(text.Substring(last)));
        return sb.ToString();
    }

    public static bool HasTmpLink(string text) {
        return !string.IsNullOrEmpty(text) && text.IndexOf("](", StringComparison.Ordinal) >= 0;
    }

    private static string EscapeTmp(string value) {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    public static string MetaPath(string activityId) {
        return $"/activity-assets/{activityId}/meta.json";
    }

    public static string WithCacheBust(string pathOrUrl) {
        if (string.IsNullOrEmpty(pathOrUrl)) return pathOrUrl;
        string sep = pathOrUrl.Contains("?") ? "&" : "?";
        return pathOrUrl + sep + "t=" + DateTime.UtcNow.Ticks;
    }

    public static IEnumerator GetIndex(Action<ActivityIndexFile> onOk, Action<string> onError) {
        string apiError = null;
        ActivityIndexFile fromApi = null;
        yield return GetJson<PlatformActivitiesResponse>(
            WithCacheBust(PlatformListPath),
            env => {
                if (env != null && env.data != null) fromApi = env.data;
            },
            err => apiError = err
        );
        if (fromApi != null) {
            onOk?.Invoke(fromApi);
            yield break;
        }
        yield return GetJson<ActivityIndexFile>(
            WithCacheBust(IndexPath),
            onOk,
            fallbackErr => onError?.Invoke(apiError ?? fallbackErr)
        );
    }

    public static IEnumerator GetJson<T>(string pathOrUrl, Action<T> onOk, Action<string> onError) {
        string url = ResolveUrl(pathOrUrl);
        using (UnityWebRequest request = UnityWebRequest.Get(url)) {
            request.timeout = 15;
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Cache-Control", "no-cache");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) {
                onError?.Invoke(request.error ?? "请求失败");
                yield break;
            }
            string text = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("<")) {
                onError?.Invoke("活动接口返回了网页而不是 JSON");
                yield break;
            }
            try {
                T data = JsonConvert.DeserializeObject<T>(text);
                if (data == null) throw new InvalidOperationException("空响应");
                onOk?.Invoke(data);
            } catch (Exception e) {
                onError?.Invoke(e.Message);
            }
        }
    }

    public static IEnumerator GetTexture(string pathOrUrl, Action<Texture2D> onOk, Action<string> onError) {
        string url = ResolveUrl(WithCacheBust(pathOrUrl));
        if (string.IsNullOrEmpty(url)) {
            onError?.Invoke("empty");
            yield break;
        }
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url)) {
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) {
                onError?.Invoke(request.error ?? "图片加载失败");
                yield break;
            }
            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null) {
                onError?.Invoke("图片解码失败");
                yield break;
            }
            onOk?.Invoke(texture);
        }
    }

    public static void DestroyTextures(List<Texture2D> textures) {
        if (textures == null) return;
        for (int i = 0; i < textures.Count; i++) {
            if (textures[i] != null) UnityEngine.Object.Destroy(textures[i]);
        }
        textures.Clear();
    }
}
