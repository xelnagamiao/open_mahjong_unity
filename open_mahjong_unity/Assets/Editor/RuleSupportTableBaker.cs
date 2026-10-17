using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Excel is the single editable source; only the baked UI ships to players.</summary>
public static class RuleSupportTableBaker {
    public sealed class TableData {
        public string[] Headers;
        public string[][] Rows;
        public bool[] Standard;
        public string Hash;
    }

    public static TableData ReadWorkbook() {
        var directory = new DirectoryInfo(Application.dataPath);
        string path = null;
        while (directory != null) {
            var candidate = Path.Combine(directory.FullName, "other", "麻将规则支持表.xlsx");
            if (File.Exists(candidate)) { path = candidate; break; }
            directory = directory.Parent;
        }
        if (path == null) throw new FileNotFoundException("未找到 other/麻将规则支持表.xlsx。");
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var copy = new MemoryStream()) { stream.CopyTo(copy); bytes = copy.ToArray(); }
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XDocument Read(string file) { using var stream = archive.GetEntry(file).Open(); return XDocument.Load(stream); }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        string[] strings = archive.GetEntry("xl/sharedStrings.xml") == null ? Array.Empty<string>()
            : Read("xl/sharedStrings.xml").Root.Elements(ns + "si").Select(e => string.Concat(e.Descendants(ns + "t").Select(t => t.Value))).ToArray();
        var cells = Read("xl/worksheets/sheet1.xml").Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r"), c => {
            string value = (string)c.Element(ns + "v") ?? "";
            return (string)c.Attribute("t") == "s" ? strings[int.Parse(value)]
                : (string)c.Attribute("t") == "inlineStr" ? string.Concat(c.Descendants(ns + "t").Select(t => t.Value)) : value;
        });
        string[] Row(int row) => Enumerable.Range(0, 10).Select(col => cells.TryGetValue(((char)('B' + col)).ToString() + row, out var value) ? value : "").ToArray();
        var headers = Row(5);
        if (!headers.SequenceEqual(new[] { "规则序号", "主规则", "首页显示名", "子规则名", "数据统计名", "规则标识", "是否提供匹配", "基础统计", "番种统计", "数据统计类型" }))
            throw new InvalidDataException("麻将规则支持表必须保留十列名称和顺序。");
        var rows = Enumerable.Range(6, cells.Keys.Select(address => int.Parse(new string(address.Where(char.IsDigit).ToArray()))).Max() - 5)
            .Select(Row).Where(row => row[5].Length > 0).ToArray();
        if (rows.Length == 0 || rows.Select(row => row[5]).Distinct().Count() != rows.Length
            || rows.Where((row, i) => row[0] != (i + 1).ToString()).Any())
            throw new InvalidDataException("支持表规则序号须逐行连续，每个子规则只能占一行。");
        var catalogPath = Path.Combine(directory.FullName, "open_mahjong_server", "server", "database", "statistics_rules.json");
        var standardIds = JObject.Parse(File.ReadAllText(catalogPath))["rules"]
            .Where(rule => (bool)rule["standard"]).Select(rule => (string)rule["sub_rule"]).ToArray();
        var standard = rows.Select(row => standardIds.Contains(row[5])).ToArray();
        using var hash = SHA256.Create();
        return new TableData { Headers = headers, Rows = rows, Standard = standard,
            Hash = Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows.Select((row, i) => string.Join("\t", row) + "\t" + standard[i]))))) };
    }

    [MenuItem("Tools/Mahjong/Main Scene/Sync Mahjong Rule Support Table")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        Bake(scene);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("麻将规则支持表已从 Excel 同步到普通和赛事创建房间面板。");
    }

    public static void Bake(Scene scene) {
        var panels = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CreatePanel>(true)).ToArray();
        if (panels.Length == 0) return;
        var data = ReadWorkbook();
        var font = RuleSupportTableFont.Prepare(data, !BuildPipeline.isBuildingPlayer);
        foreach (var panel in panels) {
            if (!BuildPipeline.isBuildingPlayer) Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Sync rule support table");
            panel.BakeRuleSupportTableUi(data.Headers, data.Rows, data.Standard, data.Hash, font);
            foreach (var component in panel.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        }
    }

    public static void Validate(Scene scene) {
        var panels = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CreatePanel>(true)).ToArray();
        if (panels.Length == 0) return;
        var data = ReadWorkbook();
        foreach (var panel in panels) panel.ValidateRuleSupportTableUi(data.Headers, data.Rows, data.Hash);
    }
}
