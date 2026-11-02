#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class FreeModeTypographyBaker
{
    static readonly string[] Paths = {
        "Assets/Resources/UI/FreeModeHud.prefab",
        "Assets/Resources/UI/FreeModeActivityPanel.prefab"
    };
    const string FontPath = "Assets/font/思源黑体/SourceHanSansSC-Regular SDF.asset";

    public static string Bake(string capturePath)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before baking free-mode typography.");
        var capture = JObject.Parse(File.ReadAllText(capturePath));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (!font) throw new InvalidOperationException("Free-mode SDF font is missing: " + FontPath);
        var loaded = new Dictionary<string, GameObject>();
        var results = new List<object>();
        try
        {
            foreach (string path in Paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                loaded.Add(path, root);
                var record = ((JArray)capture["prefabs"]).OfType<JObject>().Single(p => (string)p["assetPath"] == path);
                string migrated = EventVenueTypographyBaker.MigrateCapturedRoot(root.transform, record, font);
                ValidateRoot(root);
                results.Add(new { path, result = JObject.Parse(migrated) });
            }
            // No prefab is saved while either resource has unresolved component bindings.
            foreach (var pair in loaded)
            {
                PrefabUtility.SaveAsPrefabAsset(pair.Value, pair.Key, out bool success);
                if (!success) throw new InvalidOperationException("Could not save migrated prefab: " + pair.Key);
            }
        }
        finally
        {
            foreach (var root in loaded.Values) PrefabUtility.UnloadPrefabContents(root);
        }
        Validate();
        return JsonConvert.SerializeObject(results);
    }

    public static void Validate()
    {
        foreach (string path in Paths)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) throw new InvalidOperationException("Free-mode resource is missing: " + path);
            ValidateRoot(root);
        }
    }

    static void ValidateRoot(GameObject root)
    {
        if (root.GetComponentsInChildren<Text>(true).Length != 0 || root.GetComponentsInChildren<InputField>(true).Length != 0
            || root.GetComponentsInChildren<Dropdown>(true).Length != 0)
            throw new InvalidOperationException("Legacy UI components remain in " + root.name);
        if (root.GetComponentsInChildren<TMP_InputField>(true).Any(i => !i.textComponent || !i.textViewport)
            || root.GetComponentsInChildren<TMP_Dropdown>(true).Any(d => !d.template || !d.captionText || !d.itemText)
            || root.GetComponentsInChildren<FreeModeTileView>(true).Any(t => !t.caption)
            || root.GetComponentsInChildren<FreeModeMeldRow>(true).Any(m => !m.title))
            throw new InvalidOperationException("A free-mode UI component is not bound in " + root.name);
        var hud = root.GetComponent<FreeModeHud>();
        if (hud && (!hud.summary || !hud.voteSummary || !hud.drawerTitle || !hud.transferHint || !hud.recallHint
            || !hud.meldHint || !hud.handCount || !hud.handEmpty || !hud.scoreHint || hud.scoreRows.Length != 4
            || hud.scoreRows.Any(r => !r.name || !r.seat || !r.input)))
            throw new InvalidOperationException("Free-mode HUD bindings are incomplete.");
        var activity = root.GetComponent<FreeModeActivityPanel>();
        if (activity && (!activity.title || !activity.toggleLabel || !activity.latestLabel || !activity.emptyLabel
            || !activity.countLabel || !activity.categoryDropdown || !activity.rowTemplate
            || !activity.rowTemplate.Find("Time").GetComponent<TMP_Text>() || !activity.rowTemplate.Find("Message").GetComponent<TMP_Text>()))
            throw new InvalidOperationException("Free-mode activity bindings are incomplete.");
    }
}
#endif
