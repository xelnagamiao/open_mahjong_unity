using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 本机牌谱：WebGL 走 IndexedDB，桌面 / 安卓写 persistentDataPath。
/// 终局由服务端在 game_end_info.record_detail 下发完整牌谱后落盘。
/// </summary>
public static class LocalRecordStore {
    public const int MaxRecords = 200;
    public const string IdPrefix = "L";

    private const string DirName = "LocalRecords";
    private const string IndexFileName = "index.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None
    };

    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    static readonly List<Action> ReadyWaiters = new List<Action>();
    static readonly List<RecordInfo> MemoryIndex = new List<RecordInfo>();
    static readonly Dictionary<string, RecordDetail> MemoryDetails = new Dictionary<string, RecordDetail>();
    static bool ready;
    static bool loading;

    public static string DirectoryPath => Path.Combine(Application.persistentDataPath, DirName);

    static string IndexPath => Path.Combine(DirectoryPath, IndexFileName);

    static bool UseIndexedDb {
        get {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    static string RecordPath(string gameId) {
        return Path.Combine(DirectoryPath, SanitizeFileName(gameId) + ".json");
    }

    public static string NewLocalId() {
        string n = Guid.NewGuid().ToString("N");
        return IdPrefix + n.Substring(0, 10);
    }

    public static void EnsureReady(Action onReady) {
        if (!UseIndexedDb) {
            ready = true;
            onReady?.Invoke();
            return;
        }
        if (onReady != null) ReadyWaiters.Add(onReady);
        if (ready) {
            FlushWaiters();
            return;
        }
        if (loading) return;
        loading = true;
        LocalRecordIdbBridge.Ensure();
        LocalRecordIdbBridge.Instance.BeginLoadIndex(bytes => {
            MergeLoadedIndex(DecodeUtf8(bytes));
            FinishReady();
        }, _ => FinishReady());
    }

    public static void SavePushedDetail(RecordDetail detail) {
        try {
            if (detail == null || detail.record == null) return;
            Save(detail, detail.match_type, false);
        } catch (Exception e) {
            Debug.LogError($"写入服务端下发的本地牌谱失败: {e.Message}");
        }
    }

    static void Save(RecordDetail detail, string matchType, bool perspective) {
        if (detail == null || detail.record == null) return;
        if (string.IsNullOrEmpty(detail.game_id)) {
            detail.game_id = NewLocalId();
        }
        detail.perspective = perspective;
        if (string.IsNullOrEmpty(detail.match_type)) {
            detail.match_type = matchType ?? "";
        }
        if (string.IsNullOrEmpty(detail.created_at)) {
            detail.created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (UseIndexedDb) {
            SaveToIndexedDb(detail, matchType);
            return;
        }

        Directory.CreateDirectory(DirectoryPath);
        string json = JsonConvert.SerializeObject(detail, JsonSettings);
        File.WriteAllText(RecordPath(detail.game_id), json, Utf8);

        List<RecordInfo> index = LoadFileIndex();
        ApplyIndexUpdate(index, detail, matchType, out List<string> dropped);
        foreach (string oldId in dropped) {
            TryDeleteFile(RecordPath(oldId));
        }
        WriteFileIndex(index);
    }

    public static List<RecordInfo> ListAll() {
        if (UseIndexedDb) {
            return new List<RecordInfo>(MemoryIndex);
        }
        return LoadFileIndex();
    }

    public static RecordDetail Load(string gameId) {
        if (string.IsNullOrEmpty(gameId)) return null;
        if (UseIndexedDb) {
            return MemoryDetails.TryGetValue(gameId, out RecordDetail cached) ? cached : null;
        }
        string path = RecordPath(gameId);
        if (!File.Exists(path)) return null;
        try {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<RecordDetail>(json);
        } catch (Exception e) {
            Debug.LogError($"读取本地牌谱失败 {gameId}: {e.Message}");
            return null;
        }
    }

    public static bool TryLoad(string gameId, out RecordDetail detail) {
        detail = Load(gameId);
        return detail != null && detail.record != null;
    }

    public static void LoadAsync(string gameId, Action<RecordDetail> done) {
        if (!UseIndexedDb) {
            done?.Invoke(Load(gameId));
            return;
        }
        EnsureReady(() => {
            if (string.IsNullOrEmpty(gameId)) {
                done?.Invoke(null);
                return;
            }
            if (MemoryDetails.TryGetValue(gameId, out RecordDetail cached) && cached?.record != null) {
                done?.Invoke(cached);
                return;
            }
            if (!MemoryIndex.Exists(item => item != null && item.game_id == gameId)) {
                done?.Invoke(null);
                return;
            }
            LocalRecordIdbBridge.Ensure();
            LocalRecordIdbBridge.Instance.BeginLoad(gameId, bytes => {
                RecordDetail detail = DeserializeDetail(DecodeUtf8(bytes));
                if (detail != null && detail.record != null) {
                    MemoryDetails[gameId] = detail;
                }
                done?.Invoke(detail);
            }, _ => done?.Invoke(null));
        });
    }

    static void SaveToIndexedDb(RecordDetail detail, string matchType) {
        MemoryDetails[detail.game_id] = detail;
        ApplyIndexUpdate(MemoryIndex, detail, matchType, out List<string> dropped);
        foreach (string oldId in dropped) {
            MemoryDetails.Remove(oldId);
        }
        byte[] recordBytes = Utf8.GetBytes(JsonConvert.SerializeObject(detail, JsonSettings));
        byte[] indexBytes = Utf8.GetBytes(JsonConvert.SerializeObject(MemoryIndex, JsonSettings));
        EnsureReady(() => {
            LocalRecordIdbBridge.Ensure();
            LocalRecordIdbBridge.Instance.BeginPut(detail.game_id, recordBytes, indexBytes, () => {
                foreach (string oldId in dropped) {
                    LocalRecordIdbBridge.Instance.BeginDelete(oldId, null);
                }
            }, err => Debug.LogWarning($"写入 IndexedDB 牌谱失败: {err}"));
        });
    }

    static void ApplyIndexUpdate(List<RecordInfo> index, RecordDetail detail, string matchType, out List<string> dropped) {
        dropped = new List<string>();
        index.RemoveAll(r => r != null && r.game_id == detail.game_id);
        index.Insert(0, ToInfo(detail, matchType));
        while (index.Count > MaxRecords) {
            RecordInfo oldest = index[index.Count - 1];
            index.RemoveAt(index.Count - 1);
            if (oldest != null && !string.IsNullOrEmpty(oldest.game_id)) {
                dropped.Add(oldest.game_id);
            }
        }
    }

    static RecordInfo ToInfo(RecordDetail detail, string matchType) {
        return new RecordInfo {
            game_id = detail.game_id,
            rule = detail.rule,
            sub_rule = detail.sub_rule,
            match_type = string.IsNullOrEmpty(matchType) ? detail.match_type : matchType,
            created_at = detail.created_at,
            players = detail.players,
            is_favorite = false
        };
    }

    static List<RecordInfo> LoadFileIndex() {
        if (!File.Exists(IndexPath)) return new List<RecordInfo>();
        try {
            string json = File.ReadAllText(IndexPath, Encoding.UTF8);
            List<RecordInfo> list = JsonConvert.DeserializeObject<List<RecordInfo>>(json);
            return list ?? new List<RecordInfo>();
        } catch (Exception e) {
            Debug.LogError($"读取本地牌谱索引失败: {e.Message}");
            return new List<RecordInfo>();
        }
    }

    static void WriteFileIndex(List<RecordInfo> index) {
        File.WriteAllText(IndexPath, JsonConvert.SerializeObject(index ?? new List<RecordInfo>(), JsonSettings), Utf8);
    }

    static void TryDeleteFile(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch (Exception e) {
            Debug.LogWarning($"删除旧本地牌谱失败: {e.Message}");
        }
    }

    static string SanitizeFileName(string gameId) {
        foreach (char c in Path.GetInvalidFileNameChars()) {
            gameId = gameId.Replace(c, '_');
        }
        return gameId;
    }

    static void MergeLoadedIndex(string json) {
        List<RecordInfo> loaded = DeserializeIndex(json);
        if (loaded == null || loaded.Count == 0) return;
        var seen = new HashSet<string>();
        foreach (RecordInfo item in MemoryIndex) {
            if (item != null && !string.IsNullOrEmpty(item.game_id)) seen.Add(item.game_id);
        }
        foreach (RecordInfo item in loaded) {
            if (item == null || string.IsNullOrEmpty(item.game_id) || seen.Contains(item.game_id)) continue;
            MemoryIndex.Add(item);
            seen.Add(item.game_id);
        }
    }

    static List<RecordInfo> DeserializeIndex(string json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            return JsonConvert.DeserializeObject<List<RecordInfo>>(json);
        } catch (Exception e) {
            Debug.LogWarning($"解析 IndexedDB 牌谱索引失败: {e.Message}");
            return null;
        }
    }

    static RecordDetail DeserializeDetail(string json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            return JsonConvert.DeserializeObject<RecordDetail>(json);
        } catch (Exception e) {
            Debug.LogWarning($"解析 IndexedDB 牌谱失败: {e.Message}");
            return null;
        }
    }

    static string DecodeUtf8(byte[] bytes) {
        if (bytes == null || bytes.Length == 0) return null;
        return Utf8.GetString(bytes);
    }

    static void FinishReady() {
        ready = true;
        loading = false;
        FlushWaiters();
    }

    static void FlushWaiters() {
        if (ReadyWaiters.Count == 0) return;
        Action[] list = ReadyWaiters.ToArray();
        ReadyWaiters.Clear();
        for (int i = 0; i < list.Length; i++) {
            try {
                list[i]?.Invoke();
            } catch (Exception e) {
                Debug.LogWarning($"LocalRecordStore 就绪回调失败: {e.Message}");
            }
        }
    }
}
