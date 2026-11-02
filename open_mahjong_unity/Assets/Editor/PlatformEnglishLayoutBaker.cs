#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlatformEnglishLayoutBaker
{
    public static void Bake(Scene scene)
    {
        var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        var roots = scene.GetRootGameObjects();
        foreach (var sidebar in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "NavigateBarPanel" &&
            (t.GetComponentInParent<RoomListPanel>() || t.GetComponentInParent<RecordPanel>() || t.GetComponentInParent<SpectatorPanel>() || t.GetComponentInParent<AboutUsPanel>())).ToArray())
        {
            foreach (Transform row in sidebar)
            {
                var label = row.Find("Text (TMP)")?.GetComponent<TMP_Text>();
                if (!label || row.name == "TitlePanel") continue;
                Pad(label, 24);
                var duplicate = row.Find("Button/Text (TMP)");
                if (duplicate) UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            }
        }
        var about = roots.SelectMany(r => r.GetComponentsInChildren<AboutUsPanel>(true)).Single();
        var content = about.transform.Find("ContentPanel/Scroll View/Viewport/Content");
        foreach (Transform row in content)
            if (row.name.EndsWith("Title")) { var label = row.GetComponentInChildren<TMP_Text>(true); if (label) Pad(label, 24); }

        var docks = new RectTransform[owner.artwork.pages.Length];
        for (int i = 0; i < docks.Length; i++)
        {
            docks[i] = owner.artwork.pages[i].transform.Find("MetroBottomNavigation") as RectTransform;
            var dock = docks[i];
            if (!dock) throw new InvalidOperationException("Bottom navigation is missing: " + i);
            var layout = dock.GetComponent<LobbyDockLayout>() ?? dock.gameObject.AddComponent<LobbyDockLayout>();
            layout.buttons = dock.Cast<Transform>().Where(t => t.GetComponent<LobbyAction>()).Select(t => (RectTransform)t).ToArray();
            layout.captions = layout.buttons.Select(t => t.Find("DockLabel").GetComponent<TMP_Text>()).ToArray();
            layout.minimumWidth = i == 0 ? 100 : 112; layout.spacing = i == 0 ? 12 : 11;
            layout.referenceWidth = i == 0 ? 548 : 604; layout.referenceScale = 1;
            dock.anchorMin = dock.anchorMax = new Vector2(1, 0); dock.pivot = new Vector2(1, 0);
            dock.anchoredPosition = i == 0 ? new Vector2(-36, 24) : new Vector2(-45, 9);
            foreach (var button in layout.buttons)
            {
                foreach (RectTransform child in button)
                {
                    if (child.name.Contains("Icon"))
                    {
                        child.anchorMin = child.anchorMax = new Vector2(.5f, 1);
                        child.pivot = new Vector2(.5f, 1); child.anchoredPosition = new Vector2(0, child.anchoredPosition.y);
                    }
                    else if (child.name.Contains("Hover"))
                    { child.anchorMin = Vector2.zero; child.anchorMax = Vector2.one; child.offsetMin = child.offsetMax = Vector2.zero; }
                }
            }
            var surface = dock.Find("SidebarDockSurface") as RectTransform;
            if (surface) { surface.anchorMin = Vector2.zero; surface.anchorMax = Vector2.one; surface.offsetMin = surface.offsetMax = Vector2.zero; }
            layout.Apply();
        }
        var menuData = new SerializedObject(owner.sourceMenu.GetComponent<MeunPanel>());
        var rulebook = (RulebookPanelController)menuData.FindProperty("rulebookPanelController").objectReferenceValue;
        var fan = (GuobiaoFanTablePanel)new SerializedObject(rulebook).FindProperty("fanTablePanel").objectReferenceValue;
        var placement = fan.GetComponent<LobbyFanTablePlacement>() ?? fan.gameObject.AddComponent<LobbyFanTablePlacement>();
        placement.owner = owner; placement.docks = docks; placement.dialog = (RectTransform)fan.transform.Find("Dialog");
        placement.Apply();
        var fanData = new SerializedObject(fan);
        fanData.FindProperty("returnButton").objectReferenceValue = docks[0].GetComponentsInChildren<LobbyAction>(true).Single(a => a.action == "tutorial").GetComponent<Button>();
        fanData.ApplyModifiedPropertiesWithoutUndo();

    }

    static void Pad(TMP_Text text, float padding)
    {
        var binding = text.GetComponent<LocalizedPlatformLabel>();
        string source = (binding ? binding.key : text.text).Trim();
        text.text = source; if (binding) { binding.key = source; binding.trimOuterWhitespace = true; EditorUtility.SetDirty(binding); }
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = new Vector4(padding, 0, padding, 0);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        EditorUtility.SetDirty(text);
    }

}
#endif
