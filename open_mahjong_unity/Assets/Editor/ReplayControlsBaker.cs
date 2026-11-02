#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ReplayControlsBaker {
    public static void Validate(Scene scene) {
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameRecordManager>(true)).Single();
        var data = new SerializedObject(manager);
        var next = (Button)data.FindProperty("nextStepButton").objectReferenceValue;
        var auto = (Button)data.FindProperty("recordAutoPlayButton").objectReferenceValue;
        var live = (Button)data.FindProperty("spectatorBackLiveButton").objectReferenceValue;
        var panel = (GameObject)data.FindProperty("spectatingPanel").objectReferenceValue;
        if (!next || !auto || !live || !panel || auto.transform.parent != next.transform.parent
            || panel.transform.parent != next.transform.parent || !live.transform.IsChildOf(panel.transform))
            throw new System.InvalidOperationException("Replay and live controls must be baked into ControlPanel.");
        if (next.transform.parent.GetComponentsInChildren<TableHudPlaybackArtwork>(true).Count(a => a.name == "AutoPlay") != 1
            || !auto.transform.Find("PauseBars") || !auto.transform.Find("SourceArrow"))
            throw new System.InvalidOperationException("Replay playback graphics have not been baked.");
        foreach (var binding in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlatformFontBinding>(true)))
            if ((binding.authoredFont == TableHudTypography.Body || binding.authoredFont == TableHudTypography.Emphasis)
                && !binding.preserveAuthoredTypeface)
                throw new System.InvalidOperationException("A table font is overridden by platform typography: " + binding.name);
    }
    public static void Bake(Scene scene) {
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameRecordManager>(true)).Single();
        var data = new SerializedObject(manager);
        var next = (Button)data.FindProperty("nextStepButton").objectReferenceValue;
        var controls = (RectTransform)next.transform.parent;
        var livePanel = (GameObject)data.FindProperty("spectatingPanel").objectReferenceValue;
        livePanel.transform.SetParent(controls, false);
        var liveButton = (Button)data.FindProperty("spectatorBackLiveButton").objectReferenceValue;
        liveButton.name = "ReturnLiveButton";
        var oldPanelImage = livePanel.GetComponent<Image>();
        if (oldPanelImage) Object.DestroyImmediate(oldPanelImage);
        var oldButtonImage = liveButton.GetComponent<Image>();
        if (oldButtonImage) Object.DestroyImmediate(oldButtonImage);
        var surface = liveButton.GetComponent<TableHudSurface>() ?? liveButton.gameObject.AddComponent<TableHudSurface>();
        surface.role = TableHudTheme.SurfaceRole.Button; surface.raycastTarget = true;
        liveButton.targetGraphic = surface;
        liveButton.transition = Selectable.Transition.ColorTint;
        liveButton.onClick = new Button.ButtonClickedEvent();
        var mode = (TMP_Text)data.FindProperty("spectatorModeText").objectReferenceValue;
        mode.name = "SpectatingStateText";
        TableHudTypography.Apply(mode);
        TableHudTypography.Apply(liveButton.GetComponentInChildren<TMP_Text>(true));

        var automatic = controls.Find("AutoPlay");
        if (!automatic) {
            automatic = Object.Instantiate(next.gameObject, controls, false).transform;
            automatic.name = "AutoPlay";
        }
        var autoButton = automatic.GetComponent<Button>();
        autoButton.onClick = new Button.ButtonClickedEvent();
        data.FindProperty("recordAutoPlayButton").objectReferenceValue = autoButton;
        var autoText = autoButton.GetComponentInChildren<TMP_Text>(true);
        autoText.text = "自动播放";
        var localized = autoText.GetComponent<LocalizedPlatformLabel>();
        if (localized) { localized.key = "自动播放"; EditorUtility.SetDirty(localized); }
        data.FindProperty("recordAutoPlayButtonText").objectReferenceValue = autoText;
        data.ApplyModifiedPropertiesWithoutUndo();
        var play = automatic.Find("SourceArrow").GetComponent<Image>();
        play.sprite = Resources.LoadAll<Sprite>("image/UI/TableHud/Source/ReplayTransportSheet").First(s => s.name == "Play");
        var pause = automatic.Find("PauseBars");
        if (!pause) {
            pause = new GameObject("PauseBars", typeof(RectTransform)).transform;
            pause.SetParent(automatic, false);
            for (int i = 0; i < 2; i++) {
                var bar = new GameObject("Bar" + i, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                bar.transform.SetParent(pause, false); bar.raycastTarget = false;
                var r = bar.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
                r.sizeDelta = new Vector2(5, 22); r.anchoredPosition = new Vector2(i == 0 ? -6 : 6, 0);
            }
        }
        var artwork = automatic.GetComponent<TableHudPlaybackArtwork>() ?? automatic.gameObject.AddComponent<TableHudPlaybackArtwork>();
        var artworkData = new SerializedObject(artwork);
        artworkData.FindProperty("play").objectReferenceValue = play.gameObject;
        artworkData.FindProperty("pause").objectReferenceValue = pause.gameObject;
        artworkData.ApplyModifiedPropertiesWithoutUndo();
        artwork.SetPlaying(false);
        automatic.gameObject.SetActive(false);
        livePanel.SetActive(false);
        foreach (var name in new[] { "Group_0", "Group_1", "Group_2", "HudSurface" }) {
            var old = controls.Find(name); if (old) Object.DestroyImmediate(old.gameObject);
        }
        foreach (var mark in controls.GetComponentsInChildren<Transform>(true).Where(t => t.name == "ReadingMark").ToArray())
            Object.DestroyImmediate(mark.gameObject);
        controls.GetComponentInParent<TableHudTheme>().Apply();
        EditorUtility.SetDirty(manager);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif
