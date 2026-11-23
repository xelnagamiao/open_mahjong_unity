using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class LocalRecordStore {
    // The journal is made visible only after every candidate has been written and verified.
    // Replaying an interrupted publication replaces exactly the same files and index.
    const string MigrationStage = ".record-upgrade";
    static string DiskName(string key) {
        if (key == DeviceRecordMigration.Index)
            return "index.json";
        if (key == DeviceRecordMigration.Aliases)
            return "record-aliases.json";
        if (string.IsNullOrEmpty(key) || key.Length > 128 || key.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_') || key.StartsWith("__", StringComparison.Ordinal))
            throw new InvalidDataException("本地牌谱编号无效");
        return key + ".json";
    }

    internal static void MigrateDirectory(string directory) {
        RecoverDirectory(directory);
        var store = new Dictionary<string, string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)) {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name == "index")
                store[DeviceRecordMigration.Index] = ReadSharedFile(path);
            else if (name == "record-aliases")
                store[DeviceRecordMigration.Aliases] = ReadSharedFile(path);
            else { DiskName(name); store[name] = ReadSharedFile(path); }
        }
        string legacy = Path.Combine(directory, "MatchRecordsV2");
        string catalogPath = Path.Combine(legacy, "catalog.json");
        if (File.Exists(catalogPath)) {
            string json = ReadSharedFile(catalogPath);
            store[DeviceRecordMigration.Catalog] = json;
            var catalog = MatchRecordCodec.Parse(json);
            foreach (var entry in ((JObject)catalog["matches"]).Properties()) {
                string name = (string)entry.Value;
                if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name))
                    throw new InvalidDataException("归档文件名无效");
                string body = ReadSharedFile(Path.Combine(legacy, name));
                var record = MatchRecordCodec.Parse(body);
                if ((string)record["match_id"] != entry.Name || (string)catalog["archive_hashes"]?[entry.Name] != MatchRecordCodec.Digest(record))
                    throw new InvalidDataException("归档内容校验失败");
                store["__match_v2/" + name] = body;
            }
        }
        var retire = new JObject();
        if (Directory.Exists(legacy)) {
            foreach (var path in Directory.EnumerateFiles(legacy)) {
                string file = Path.GetFileName(path);
                if (file.EndsWith(".match.json", StringComparison.Ordinal) && !store.ContainsKey("__match_v2/" + file))
                    throw new InvalidDataException("存在未核对的旧归档：" + file);
                if (file == "catalog.json" || file == "browser-import.json" || file.EndsWith(".match.json", StringComparison.Ordinal)
                    || file.EndsWith(".journal.json", StringComparison.Ordinal)) {
                    if (!store.ContainsKey(DeviceRecordMigration.Catalog))
                        throw new InvalidDataException("旧归档缺少目录：" + file);
                    string old = ReadSharedFile(path);
                    if (file.EndsWith(".journal.json", StringComparison.Ordinal)) {
                        string state = (string)MatchRecordCodec.Parse(old)["status"];
                        if (state != "published" && state != "rolled_back") throw new InvalidDataException("旧迁移仍在进行：" + file);
                    }
                    if (file == "browser-import.json") {
                        var package = MatchRecordCodec.Parse(old);
                        var catalog = MatchRecordCodec.Parse(store[DeviceRecordMigration.Catalog]);
                        if ((string)package["format"] != "omu.localRecords.import.v2" || !(package["archives"] is JObject archives))
                            throw new InvalidDataException("旧浏览器导入包格式未知");
                        foreach (var item in archives.Properties()) {
                            string mid = (string)item.Value["match_id"];
                            if (mid == null || (string)catalog["matches"]?[mid] != item.Name ||
                                (string)catalog["archive_hashes"]?[mid] != MatchRecordCodec.Digest(item.Value))
                                throw new InvalidDataException("旧浏览器导入包未核对：" + item.Name);
                        }
                    }
                    retire[file] = TextChecksum(old);
                }
            }
        }
        var plan = DeviceRecordMigration.Prepare(store);
        if (plan.Writes.Count != 0) PublishDirectory(directory, plan, retire);
    }

    static void PublishDirectory(string directory, DeviceRecordMigration.Plan plan, JObject retire = null) {
        string stage = Path.Combine(directory, MigrationStage);
        if (File.Exists(Path.Combine(stage, "journal")))
            RecoverDirectory(directory);
        Directory.CreateDirectory(stage);
        var writes = new JObject();
        foreach (var p in plan.Writes) {
            string name = DiskName(p.Key);
            var path = Path.Combine(stage, name);
            WriteFileAtomically(path, p.Value);
            if (!string.Equals(ReadSharedFile(path), p.Value, StringComparison.Ordinal))
                throw new IOException("牌谱暂存校验失败");
            writes[name] = TextChecksum(p.Value);
        }
        var journal = new JObject { ["writes"] = writes, ["delete"] = new JArray(plan.Deletes.Where(k => !k.StartsWith("__", StringComparison.Ordinal)).Distinct().Select(DiskName)), ["retire"] = retire };
        WriteFileAtomically(Path.Combine(stage, "journal"), journal.ToString(Formatting.None));
        RecoverDirectory(directory);
    }

    static string TextChecksum(string text) {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Utf8.GetBytes(text)));
    }

    static void RecoverDirectory(string directory) {
        string stage = Path.Combine(directory, MigrationStage), path = Path.Combine(stage, "journal");
        if (!File.Exists(path))
            return;
        var journal = MatchRecordCodec.Parse(ReadSharedFile(path));
        var writes = (JObject)journal["writes"];
        foreach (var p in writes.Properties()) {
            if (p.Name != Path.GetFileName(p.Name) || !p.Name.EndsWith(".json", StringComparison.Ordinal))
                throw new InvalidDataException("暂存路径无效");
            string contents = ReadSharedFile(Path.Combine(stage, p.Name));
            if (TextChecksum(contents) != (string)p.Value)
                throw new InvalidDataException("暂存牌谱已改变");
        }
        foreach (var p in writes.Properties()) {
            var to = Path.Combine(directory, p.Name);
            if (!File.Exists(to) || TextChecksum(ReadSharedFile(to)) != (string)p.Value)
                WriteFileAtomically(to, ReadSharedFile(Path.Combine(stage, p.Name)));
            if (TextChecksum(ReadSharedFile(to)) != (string)p.Value)
                throw new IOException("发布牌谱校验失败");
        }
        foreach (string name in (JArray)journal["delete"]) {
            if (name != Path.GetFileName(name) || !name.EndsWith(".json", StringComparison.Ordinal))
                throw new InvalidDataException("旧牌谱路径无效");
            if (writes[name] == null)
                File.Delete(Path.Combine(directory, name));
        }
        if (journal["retire"] is JObject retired) {
            string legacy = Path.Combine(directory, "MatchRecordsV2");
            if (Directory.Exists(legacy)) {
                foreach (var old in retired.Properties()) {
                    if (old.Name != Path.GetFileName(old.Name))
                        throw new InvalidDataException("旧归档路径无效");
                    string file = Path.Combine(legacy, old.Name);
                    if (!File.Exists(file))
                        continue;
                    if (TextChecksum(ReadSharedFile(file)) != (string)old.Value)
                        throw new IOException("旧归档已改变：" + old.Name);
                    File.Delete(file);
                }
                if (!Directory.EnumerateFileSystemEntries(legacy).Any())
                    Directory.Delete(legacy);
            }
        }
        File.Delete(path);
        foreach (var p in writes.Properties())
            File.Delete(Path.Combine(stage, p.Name));
        if (!Directory.EnumerateFileSystemEntries(stage).Any())
            Directory.Delete(stage);
    }
}
