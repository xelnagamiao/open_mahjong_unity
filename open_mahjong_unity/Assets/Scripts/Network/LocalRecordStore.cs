using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 本机牌谱：WebGL 走 IndexedDB，桌面 / 安卓写 persistentDataPath。
/// 终局由服务端在 game_end_info.record_detail 下发完整牌谱后落盘。
/// </summary>
public static partial class LocalRecordStore {
    public const int MaxRecords = 200;
    public const string IdPrefix = "L";

    private const string DirName = "LocalRecords";
    private const string IndexFileName = "index.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Formatting = Formatting.None
    };

    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    static readonly List<Action> ReadyWaiters = new List<Action>();
    static readonly List<RecordInfo> MemoryIndex = new List<RecordInfo>();
    static bool ready;
    static bool loading;

    public static string DirectoryPath {
        get {
#if UNITY_EDITOR
            string isolated = UnityEditor.SessionState.GetString("OpenMahjong.Validation.LocalRecords", "");
            if (!string.IsNullOrEmpty(isolated)) return isolated;
#endif
            return Path.Combine(Application.persistentDataPath, DirName);
        }
    }

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
        if (onReady != null) ReadyWaiters.Add(onReady);
        if (ready) { FlushWaiters(); return; }
        if (loading) return;
        loading = true;
        if (!UseIndexedDb) { PrepareNative(DirectoryPath); return; }
        LocalRecordIdbBridge.Ensure();
        var bridge = LocalRecordIdbBridge.Instance;
        void Failed(string error) {
            loading = false;
            ReadyWaiters.Clear();
            Debug.LogError("本地牌谱读取失败，已保留原数据：" + error);
        }
        void ReadIndex() => bridge.BeginLoadIndex(bytes => {
            try { MergeLoadedIndex(DecodeUtf8(bytes)); FinishReady(); }
            catch (Exception e) { Failed(e.Message); }
        }, Failed);
        void Upgrade(int attempt) => bridge.BeginLoad(DeviceRecordMigration.Aliases, bytes => {
            try {
                if (bytes != null) { MatchRecordCodec.Parse(DecodeUtf8(bytes)); ReadIndex(); return; }
                bridge.BeginSnapshot(null, (token,snapshot) => {
                    try {
                        var store = JsonConvert.DeserializeObject<Dictionary<string, string>>(DecodeUtf8(snapshot), JsonSettings);
                        bridge.StartCoroutine(PrepareBrowserMigration(store, plan =>
                            bridge.BeginCommit(token, plan, ReadIndex, error => {
                                if (error == "conflict" && attempt < 3) Upgrade(attempt + 1);
                                else Failed(error);
                            }), error => {
                                bridge.ReleaseSnapshot(token);
                                Failed(error);
                            }));
                    } catch (Exception e) { bridge.ReleaseSnapshot(token); Failed(e.Message); }
                }, Failed);
            } catch (Exception e) { Failed(e.Message); }
        }, Failed);
        Upgrade(0);
    }

    static IEnumerator PrepareBrowserMigration(Dictionary<string, string> store,
            Action<DeviceRecordMigration.Plan> commit, Action<string> failed) {
        // Let the IndexedDB callback and its full UTF-8 snapshot leave the WASM stack.
        yield return null;
        var plan = new DeviceRecordMigration.Plan();
        var steps = DeviceRecordMigration.PrepareSteps(store, plan);
        while (true) {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception e) { failed(e.Message); yield break; }
            if (!more) break;
            yield return null;
        }
        steps = null;
        store = null;
        yield return null;
        try { commit(plan); }
        catch (Exception e) { failed(e.Message); }
    }

    static async void PrepareNative(string directory) {
        try {
            // Do not block the game loop on the first upgrade of a large player library.
            if (!File.Exists(Path.Combine(directory,"record-aliases.json")) || File.Exists(Path.Combine(directory,MigrationStage,"journal"))) {
                await System.Threading.Tasks.Task.Run(() => {
                    Directory.CreateDirectory(directory);
                    using (AcquireFileWriteLock(directory)) MigrateDirectory(directory);
                });
            }
            FinishReady();
        } catch (Exception e) {
            loading=false; ReadyWaiters.Clear();
            Debug.LogError("本地牌谱升级失败，已保留原数据：" + e.Message);
        }
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
        if (IsDuplicateRecord(detail)) return;
        if (string.IsNullOrEmpty(detail.game_id)) {
            detail.game_id = NewLocalId();
            detail.cloud_saved = false;
        }
        detail.perspective = perspective;
        if (string.IsNullOrEmpty(detail.match_type)) {
            detail.match_type = matchType ?? "";
        }
        if (string.IsNullOrEmpty(detail.created_at)) {
            detail.created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (!MatchRecordCodec.IsUnified(detail.record)) throw new InvalidDataException("服务端下发了未知牌谱格式");
        string canonical = MatchRecordCodec.GameId(detail.record);
        if (string.IsNullOrEmpty(canonical)) throw new InvalidDataException("牌谱编号缺失");
        detail.game_id = canonical;
        EnsureReady(() => {
            if (UseIndexedDb) { SaveToIndexedDb(detail, matchType); return; }
            using (AcquireFileWriteLock()) {
                RecoverDirectory(DirectoryPath);
                var existing = ReadFileDetail(detail.game_id);
                if (existing != null) MergeStoredDetail(existing, detail);
                var aliases = LoadFileAliases();
                DeviceRecordMigration.AddAliases(aliases, detail);
                var index = LoadFileIndex();
                ApplyIndexUpdate(index, detail, matchType, out var dropped);
                PruneAliases(aliases, dropped);
                var plan = new DeviceRecordMigration.Plan();
                plan.Writes[canonical] = JsonConvert.SerializeObject(detail, JsonSettings);
                plan.Writes[DeviceRecordMigration.Index] = JsonConvert.SerializeObject(index, JsonSettings);
                plan.Writes[DeviceRecordMigration.Aliases] = aliases.ToString(Formatting.None);
                plan.Deletes.AddRange(dropped);
                PublishDirectory(DirectoryPath, plan);
            }
        });
    }

    private static bool IsDuplicateRecord(RecordDetail detail) {
        if (!string.IsNullOrEmpty(detail.duplicate_key)) return true;
        var record = detail.record;
        if (MatchRecordCodec.IsUnified(record)) {
            foreach (var p in ((JObject)record["tables"]).Properties()) {
                var title = p.Value["payload"] is JObject payload ? payload["game_title"] as JObject : null;
                if (title == null) continue;
                if (!string.IsNullOrEmpty((string)title["duplicate_key"])) return true;
                if ((string)title["wall_source"] != "training" && ((bool?)title["is_duplicate"] == true || detail.is_duplicate)) return true;
            }
            return false;
        }
        return detail.is_duplicate;
    }

    public static List<RecordInfo> ListAll() {
        if (UseIndexedDb) {
            return new List<RecordInfo>(MemoryIndex);
        }
        EnsureReady(null);
        if (!ready) return new List<RecordInfo>();
        using (AcquireFileWriteLock()) { RecoverDirectory(DirectoryPath); return LoadFileIndex(); }
    }

    public static RecordDetail Load(string gameId) {
        if (string.IsNullOrEmpty(gameId)) return null;
        if (UseIndexedDb) return null; // IndexedDB reads use LoadAsync.
        EnsureReady(null);
        if (!ready) return null;
        using (AcquireFileWriteLock()) {
            RecoverDirectory(DirectoryPath);
            var alias = LoadFileAliases()[gameId] as JObject;
            var detail = ReadFileDetail((string)alias?["game_id"] ?? gameId);
            SelectAlias(detail, alias);
            return detail;
        }
    }

    static JObject LoadFileAliases() => MatchRecordCodec.Parse(ReadSharedFile(Path.Combine(DirectoryPath, "record-aliases.json")));

    static void MergeStoredDetail(RecordDetail previous, RecordDetail incoming) {
        incoming.record = DeviceRecordMigration.Merge(previous.record, incoming.record);
        incoming.cloud_saved |= previous.cloud_saved;
        incoming.local_table_metadata ??= previous.local_table_metadata;
        if ((string)incoming.record["match_format"] == "paired_tables_2v2")
            incoming.players = ((JObject)incoming.record["tables"]).Properties().SelectMany(t =>
                (t.Value["players"] as JArray ?? new JArray()).Select(p => {
                    var player = p.ToObject<PlayerRecordInfo>();
                    player.table_id = t.Name;
                    return player;
                })).ToArray();
    }

    static RecordDetail ReadFileDetail(string id) {
        string path = RecordPath(id);
        return File.Exists(path) ? DeserializeDetail(ReadSharedFile(path)) : null;
    }

    static void SelectAlias(RecordDetail detail, JObject alias) {
        if (detail == null || alias == null) return;
        detail.selected_table_id = (string)alias["table_id"];
        detail.legacy_cursor_map = alias;
    }

    public static bool TryLoad(string gameId, out RecordDetail detail) {
        detail = Load(gameId);
        return detail?.record != null;
    }

    public static void LoadAsync(string gameId, Action<RecordDetail> done) {
        if (!UseIndexedDb) { EnsureReady(() => done?.Invoke(Load(gameId))); return; }
        EnsureReady(() => {
            if (string.IsNullOrEmpty(gameId)) { done?.Invoke(null); return; }
            var bridge = LocalRecordIdbBridge.Instance;
            bridge.BeginLoad(DeviceRecordMigration.Aliases, links => {
                var alias = MatchRecordCodec.Parse(DecodeUtf8(links))[gameId] as JObject;
                string id = (string)alias?["game_id"] ?? gameId;
                bridge.BeginLoad(id, bytes => {
                    var detail = DeserializeDetail(DecodeUtf8(bytes));
                    SelectAlias(detail, alias);
                    done?.Invoke(detail);
                }, _ => done?.Invoke(null));
            }, _ => done?.Invoke(null));
        });
    }

    static void SaveToIndexedDb(RecordDetail detail, string matchType) {
        var bridge = LocalRecordIdbBridge.Instance;
        // Read and compare the same store snapshot used for the write so another tab cannot overwrite a completed table.
        void Write(int attempt) {
            bridge.BeginSnapshot(detail.game_id, (token,bytes) => {
                try {
                    var store = MatchRecordCodec.Parse(DecodeUtf8(bytes)).ToObject<Dictionary<string,string>>();
                    var index = store.TryGetValue(DeviceRecordMigration.Index, out var text) ? DeserializeIndex(text) : new List<RecordInfo>();
                    var aliases = store.TryGetValue(DeviceRecordMigration.Aliases, out var a) ? MatchRecordCodec.Parse(a) : new JObject();
                    if (store.TryGetValue(detail.game_id, out var previous))
                        MergeStoredDetail(DeserializeDetail(previous), detail);
                    DeviceRecordMigration.AddAliases(aliases, detail);
                    ApplyIndexUpdate(index, detail, matchType, out var dropped);
                    PruneAliases(aliases, dropped);
                    var plan = new DeviceRecordMigration.Plan();
                    plan.Writes[detail.game_id] = JsonConvert.SerializeObject(detail, JsonSettings);
                    plan.Writes[DeviceRecordMigration.Index] = JsonConvert.SerializeObject(index, JsonSettings);
                    plan.Writes[DeviceRecordMigration.Aliases] = aliases.ToString(Formatting.None);
                    plan.Deletes.AddRange(dropped);
                    bridge.BeginCommit(token, plan, () => {
                        MemoryIndex.Clear(); MemoryIndex.AddRange(index);
                    }, err => { if (err=="conflict" && attempt<3) Write(attempt+1); else Debug.LogError("写入本地牌谱失败："+err); });
                } catch (Exception e) { bridge.ReleaseSnapshot(token); Debug.LogError("写入本地牌谱失败："+e.Message); }
            }, err => Debug.LogError("读取本地牌谱失败："+err));
        }
        Write(0);
    }

    static void PruneAliases(JObject aliases, List<string> dropped) {
        foreach (var p in aliases.Properties().Where(p => dropped.Contains((string)p.Value["game_id"])).ToArray()) p.Remove();
    }

    static void ApplyIndexUpdate(List<RecordInfo> index, RecordDetail detail, string matchType, out List<string> dropped) {
        dropped = new List<string>();
        var previous = index.FirstOrDefault(r => r != null && r.game_id == detail.game_id);
        var info = ToInfo(detail, matchType);
        if (previous != null) {
            info.is_favorite = previous.is_favorite;
            info.metadata = previous.metadata;
            info.match_queue_type ??= previous.match_queue_type;
            index.Remove(previous);
        }
        index.Insert(0, info);
        while (index.Count > MaxRecords) {
            RecordInfo oldest = index[index.Count - 1];
            index.RemoveAt(index.Count - 1);
            if (oldest != null && !string.IsNullOrEmpty(oldest.game_id)) {
                dropped.Add(oldest.game_id);
            }
        }
    }

    internal static RecordInfo ToInfo(RecordDetail detail, string matchType) {
        var keys = RuleNameDictionary.RecordKeys(detail);
        return new RecordInfo {
            game_id = detail.game_id,
            rule = keys.rule,
            sub_rule = keys.subRule,
            match_type = string.IsNullOrEmpty(matchType) ? detail.match_type : matchType,
            created_at = detail.created_at,
            players = detail.players,
            match_summary = (string)detail.record?["match_format"] != "paired_tables_2v2" ? null : new Newtonsoft.Json.Linq.JObject {
                ["match_id"] = detail.record["match_id"]?.DeepClone(),
                ["teams"] = detail.record["teams"]?.DeepClone(),
                ["result"] = detail.record["result"]?.DeepClone(),
                ["personal_imp"] = PersonalImpSummary.Build(detail.record),
                ["tables"] = new Newtonsoft.Json.Linq.JObject(
                    ((Newtonsoft.Json.Linq.JObject)detail.record["tables"]).Properties().Select(t =>
                        new Newtonsoft.Json.Linq.JProperty(t.Name, new Newtonsoft.Json.Linq.JObject {
                            ["players"] = t.Value["players"]?.DeepClone(),
                            ["payload"] = new JObject { ["game_title"] = new JObject { ["team_by_player_index"] = (t.Value["payload"] as JObject)?["game_title"]?["team_by_player_index"]?.DeepClone() } }
                        })))
            },
            is_favorite = false
        };
    }

    static List<RecordInfo> LoadFileIndex() {
        if (!File.Exists(IndexPath)) return new List<RecordInfo>();
        return DeserializeIndex(ReadSharedFile(IndexPath));
    }

    static FileStream AcquireFileWriteLock(string directory = null) {
        string lockPath = Path.Combine(directory ?? DirectoryPath, ".write.lock");
        for (int attempt = 0; ; attempt++) {
            try {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) when (attempt < 200) {
                System.Threading.Thread.Sleep(10);
            }
        }
    }

    static string ReadSharedFile(string path) {
        // Readers keep a complete old or new file while another client replaces it.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8)) {
            return reader.ReadToEnd();
        }
    }

    static void WriteFileAtomically(string path, string json) {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, json, Utf8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        } finally {
            if (File.Exists(temporary)) File.Delete(temporary);
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
        return string.IsNullOrEmpty(json) ? new List<RecordInfo>() :
            JsonConvert.DeserializeObject<List<RecordInfo>>(json, JsonSettings) ?? throw new InvalidDataException("本地牌谱索引无效");
    }

    static RecordDetail DeserializeDetail(string json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            return JsonConvert.DeserializeObject<RecordDetail>(json, JsonSettings);
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
