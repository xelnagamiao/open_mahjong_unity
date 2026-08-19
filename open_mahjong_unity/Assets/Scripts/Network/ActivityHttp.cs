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
public class ActivityDetail {
    public string id;
    public string title;
    public string body;
    public string cover_url;
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
#if UNITY_EDITOR
        return "http://localhost:3000" + path;
#elif UNITY_WEBGL && !UNITY_EDITOR
        return path;
#else
        string root = ConfigManager.webUrl;
        if (string.IsNullOrEmpty(root)) root = "https://salasasa.cn";
        return root.TrimEnd('/') + path;
#endif
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
