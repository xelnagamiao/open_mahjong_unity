#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 仅菜单：把牌边手改布局从 8/24 22:27 磁盘备份写回当前打开的 MainScene。
/// 不自动执行。不改赛事/基地节点。
/// </summary>
public static class HandSceneLayoutRestorer
{
    const string UndoName = "还原牌边手改布局";
    const string JsonRel = ".om_workspace/tmp/edge_layout_working.json";

    [MenuItem("Tools/Game/按手改备份还原牌边布局")]
    public static void RestoreEdgeFromMenu()
    {
        int n = RestoreEdge();
        EditorUtility.DisplayDialog(
            "牌边手改布局",
            n > 0
                ? "已按 8/24 22:27 手改备份还原牌边按钮尺寸、父级，并补上底图。\n赛事/基地的 EventDetailRoot 与该备份一致，没有覆盖。\n请立刻 Ctrl+S。"
                : "找不到 EdgeSection 或备份 JSON。请先打开 MainScene。",
            "好的");
    }

    public static int RestoreEdge()
    {
        GameObject edgeGo = FindInOpenScenes("EdgeSection");
        if (edgeGo == null) return 0;
        List<LayoutItem> items = LoadItems();
        if (items == null || items.Count == 0) return 0;

        Undo.RegisterFullObjectHierarchyUndo(edgeGo, UndoName);
        DisableLayoutGroups(edgeGo.transform);

        int applied = 0;
        foreach (LayoutItem item in items)
        {
            if (string.IsNullOrEmpty(item.name) || item.name == "EdgeSection") continue;
            if (item.name == "FrontTexFollowTableBg" || item.name == "FrontTexFollowTableBgToEdge") continue;
            Transform t = FindDeep(edgeGo.transform, item.name);
            if (t == null) continue;
            Transform parent = ResolveParent(edgeGo.transform, item.parent);
            if (parent != null && t.parent != parent)
            {
                Undo.SetTransformParent(t, parent, UndoName);
            }
            RectTransform rt = t as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = ParseVec2(item.amin);
                rt.anchorMax = ParseVec2(item.amax);
                rt.pivot = ParseVec2(item.pivot);
                rt.anchoredPosition = ParseVec2(item.pos);
                rt.sizeDelta = ParseVec2(item.size);
                EditorUtility.SetDirty(rt);
            }
            ApplyImage(t, item);
            applied++;
        }

        Transform backRow = FindDeep(edgeGo.transform, "BackEdgeRow");
        if (backRow != null && backRow.childCount == 0)
        {
            backRow.gameObject.SetActive(false);
        }

        EditorUtility.SetDirty(edgeGo);
        if (edgeGo.scene.IsValid()) EditorSceneManager.MarkSceneDirty(edgeGo.scene);
        return applied;
    }

    static void DisableLayoutGroups(Transform root)
    {
        LayoutGroup[] groups = root.GetComponentsInChildren<LayoutGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] == null) continue;
            string owner = groups[i].gameObject.name;
            if (owner != "FrontEdgeRow" && owner != "BackEdgeRow") continue;
            Undo.RecordObject(groups[i], UndoName);
            groups[i].enabled = false;
            EditorUtility.SetDirty(groups[i]);
        }
    }

    static Transform ResolveParent(Transform edgeRoot, string parentName)
    {
        if (string.IsNullOrEmpty(parentName) || parentName == "EdgeSection") return edgeRoot;
        Transform found = FindDeep(edgeRoot, parentName);
        return found != null ? found : edgeRoot;
    }

    static void ApplyImage(Transform t, LayoutItem item)
    {
        Image image = t.GetComponent<Image>();
        if (image == null) return;
        Undo.RecordObject(image, UndoName);
        Color c;
        if (TryParseColor(item.color, out c) && c.a > 0.01f)
        {
            image.color = c;
        }
        image.enabled = true;
        EditorUtility.SetDirty(image);
    }

    static List<LayoutItem> LoadItems()
    {
        string path = JsonPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return FallbackItems();
        try
        {
            string json = File.ReadAllText(path);
            LayoutItem[] arr = JsonUtility.FromJson<LayoutList>("{\"items\":" + json + "}").items;
            if (arr != null && arr.Length > 0) return new List<LayoutItem>(arr);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HandSceneLayoutRestorer] JSON 解析失败，改用手写回退: " + e.Message);
        }
        return FallbackItems();
    }

    static List<LayoutItem> FallbackItems()
    {
        return new List<LayoutItem>
        {
            Item("FrontEdgeRow", "EdgeSection", "-170,-87.4821", "150,44", "0.5,0.5"),
            Item("FrontEdgeModeIndependent", "FrontEdgeRow", "0,0", "220,44", "0,1", "0,1", "0,1"),
            Item("FrontEdgeModeFollowTableBg", "FrontEdgeRow", "232,0", "280,44", "0,1", "0,1", "0,1"),
            Item("FrontEdgeModeFollowBackEdge", "FrontEdgeRow", "524,0", "280,44", "0,1", "0,1", "0,1"),
            Item("BackEdgeModeIndependent", "EdgeSection", "-170,-31.482098", "150,44", "0.5,0.5"),
            Item("BackEdgeModeFollowBack", "EdgeSection", "-10.000025,-31.482046", "150,44", "0.5,0.5"),
            Item("BackEdgeModeFollowFront", "EdgeSection", "149.99997,-31.482046", "150,44", "0.5,0.5"),
            Item("RestoreFrontEdgeButton", "EdgeSection", "256,101.843185", "130,44", "0.5,0.5"),
            Item("RestoreBackEdgeButton", "BackEdgeSection", "248,-54", "130,44", "0.5,0.5"),
        };
    }

    static LayoutItem Item(string name, string parent, string pos, string size, string anc, string amax = null, string pivot = null)
    {
        string[] p = pos.Split(',');
        string[] s = size.Split(',');
        string[] a = anc.Split(',');
        string[] b = (amax ?? anc).Split(',');
        string[] v = (pivot ?? anc).Split(',');
        return new LayoutItem
        {
            name = name,
            parent = parent,
            pos = "{x: " + p[0] + ", y: " + p[1] + "}",
            size = "{x: " + s[0] + ", y: " + s[1] + "}",
            amin = "{x: " + a[0] + ", y: " + a[1] + "}",
            amax = "{x: " + b[0] + ", y: " + b[1] + "}",
            pivot = "{x: " + v[0] + ", y: " + v[1] + "}",
            color = "",
        };
    }

    static string JsonPath()
    {
        string local = Path.Combine(Application.dataPath, "Scripts", "Editor", "edge_layout_working.json");
        if (File.Exists(local)) return local;
        DirectoryInfo project = Directory.GetParent(Application.dataPath);
        if (project == null) return null;
        DirectoryInfo repo = project.Parent;
        string root = repo != null ? repo.FullName : project.FullName;
        return Path.Combine(root, JsonRel.Replace('/', Path.DirectorySeparatorChar));
    }

    static Vector2 ParseVec2(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return Vector2.zero;
        Match m = Regex.Match(raw, @"x:\s*(-?[0-9.]+).*y:\s*(-?[0-9.]+)");
        if (!m.Success) return Vector2.zero;
        float x, y;
        float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        return new Vector2(x, y);
    }

    static bool TryParseColor(string raw, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrEmpty(raw)) return false;
        Match m = Regex.Match(raw, @"r:\s*(-?[0-9.]+).*g:\s*(-?[0-9.]+).*b:\s*(-?[0-9.]+).*a:\s*(-?[0-9.]+)");
        if (!m.Success) return false;
        float r, g, b, a;
        float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
        float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out g);
        float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b);
        float.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a);
        color = new Color(r, g, b, a);
        return true;
    }

    static GameObject FindInOpenScenes(string name)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform found = FindDeep(roots[r].transform, name);
                if (found != null) return found.gameObject;
            }
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform nested = FindDeep(root.GetChild(i), name);
            if (nested != null) return nested;
        }
        return null;
    }

    [Serializable]
    class LayoutList
    {
        public LayoutItem[] items;
    }

    [Serializable]
    class LayoutItem
    {
        public string name;
        public string go;
        public string parent;
        public string pos;
        public string size;
        public string amin;
        public string amax;
        public string pivot;
        public string active;
        public string sprite;
        public string color;
    }
}
#endif
