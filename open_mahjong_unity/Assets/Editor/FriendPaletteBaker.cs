#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FriendPaletteBaker
{
    static readonly Color LabelColor = new Color32(50, 50, 50, 255);
    static readonly string[] FriendLabels = {
        "GameObject/SpectatorButton/Text (TMP)",
        "GameObject/RealTimeSpectatorButton (1)/Text (TMP)",
        "GameObject/CancelFollowerButton/Text (TMP)"
    };
    static readonly string[] RequestLabels = { "GameObject/Yes/Text (TMP)", "GameObject/No/Text (TMP)" };
    static readonly string[] FollowingLabels = {
        "GameObject/SpectatorButton/Text (TMP)", "GameObject/CancelFollowerButton/Text (TMP)"
    };
    static readonly string[] PrefabPaths = {
        "Assets/Prefabs/GameScene/FriendItem.prefab",
        "Assets/Prefabs/GameScene/FriendRequestItem.prefab",
        "Assets/Prefabs/GameScene/FolloweringItem.prefab"
    };

    public static int Bake(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before baking the friend panel.");
        int changed = 0;
        foreach (string path in PrefabPaths)
        {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int count = Apply(prefab.transform, PathsFor(prefab));
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(prefab, path);
                changed += count;
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        int sceneChanges = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var panel in root.GetComponentsInChildren<FriendPanel>(true)) sceneChanges += BakePanelLayout(panel);
            foreach (var item in root.GetComponentsInChildren<FriendItem>(true)) sceneChanges += Apply(item.transform, FriendLabels);
            foreach (var item in root.GetComponentsInChildren<FriendRequestItem>(true)) sceneChanges += Apply(item.transform, RequestLabels);
            foreach (var item in root.GetComponentsInChildren<FollowingItem>(true)) sceneChanges += Apply(item.transform, FollowingLabels);
        }
        if (sceneChanges > 0) EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
        return changed + sceneChanges;
    }

    static readonly string[] ListPanelFields = { "friendsPanel", "followingPanel", "requestsPanel" };

    static ScrollRect ListScroll(SerializedObject panel, string field)
    {
        var page = panel.FindProperty(field).objectReferenceValue as GameObject;
        var scroll = page ? page.GetComponentInChildren<ScrollRect>(true) : null;
        if (!scroll || !scroll.viewport || !scroll.verticalScrollbar)
            throw new InvalidOperationException("Friend list scroll references are missing: " + field);
        if (scroll.horizontal || scroll.horizontalScrollbar)
            throw new InvalidOperationException("Friend list must be vertical-only: " + field);
        return scroll;
    }

    static int BakePanelLayout(FriendPanel panel)
    {
        var serialized = new SerializedObject(panel);
        int changed = 0;
        foreach (string field in ListPanelFields)
        {
            var scroll = ListScroll(serialized, field);
            changed += FlushListBottom(scroll.viewport);
            changed += FlushListBottom((RectTransform)scroll.verticalScrollbar.transform);
        }
        var count = serialized.FindProperty("friendCountText").objectReferenceValue as TMP_Text;
        if (!count) throw new InvalidOperationException("Friend capacity label is missing.");
        string initialCount = "0/" + FriendPanel.DefaultFriendCapacity;
        if (count.text != initialCount)
        {
            count.text = initialCount;
            EditorUtility.SetDirty(count);
            if (PrefabUtility.IsPartOfPrefabInstance(count)) PrefabUtility.RecordPrefabInstancePropertyModifications(count);
            changed++;
        }
        return changed;
    }

    static int FlushListBottom(RectTransform rect)
    {
        if (!Mathf.Approximately(rect.anchorMin.y, 0f) || !Mathf.Approximately(rect.anchorMax.y, 1f))
            throw new InvalidOperationException("Friend list must stretch vertically: " + rect.name);
        var bottom = rect.offsetMin;
        if (Mathf.Approximately(bottom.y, 0f)) return 0;
        bottom.y = 0f;
        rect.offsetMin = bottom;
        EditorUtility.SetDirty(rect);
        if (PrefabUtility.IsPartOfPrefabInstance(rect)) PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        return 1;
    }

    static void ValidatePanelLayout(FriendPanel panel)
    {
        var serialized = new SerializedObject(panel);
        foreach (string field in ListPanelFields)
        {
            var scroll = ListScroll(serialized, field);
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            if (!Mathf.Approximately(scroll.viewport.offsetMin.y, 0f) || !Mathf.Approximately(bar.offsetMin.y, 0f))
                throw new InvalidOperationException("Friend list retains an unused horizontal scrollbar gap: " + field);
            if (!Mathf.Approximately(scroll.viewport.offsetMax.y, bar.offsetMax.y))
                throw new InvalidOperationException("Friend list viewport and scrollbar tops do not align: " + field);
        }
        var count = serialized.FindProperty("friendCountText").objectReferenceValue as TMP_Text;
        if (!count || count.text != "0/" + FriendPanel.DefaultFriendCapacity)
            throw new InvalidOperationException("Friend capacity label is not baked to the default capacity.");
    }

    static string[] PathsFor(GameObject root)
    {
        if (root.GetComponent<FriendItem>()) return FriendLabels;
        if (root.GetComponent<FriendRequestItem>()) return RequestLabels;
        if (root.GetComponent<FollowingItem>()) return FollowingLabels;
        throw new InvalidOperationException("Unrecognized friend row: " + root.name);
    }

    static TMP_Text Label(Transform root, string path)
    {
        var node = root.Find(path);
        var text = node ? node.GetComponent<TMP_Text>() : null;
        if (!text) throw new InvalidOperationException("Friend action label is missing: " + root.name + "/" + path);
        return text;
    }

    static int Apply(Transform root, string[] paths)
    {
        int changed = 0;
        foreach (string path in paths)
        {
            var text = Label(root, path);
            if (text.color == LabelColor) continue;
            text.color = LabelColor;
            EditorUtility.SetDirty(text);
            if (PrefabUtility.IsPartOfPrefabInstance(text)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            changed++;
        }
        return changed;
    }

    public static void Validate(Scene scene)
    {
        foreach (string path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new InvalidOperationException("Friend row prefab is missing: " + path);
            ValidateLabels(prefab.transform, PathsFor(prefab));
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var panel in root.GetComponentsInChildren<FriendPanel>(true)) ValidatePanelLayout(panel);
            foreach (var item in root.GetComponentsInChildren<FriendItem>(true)) ValidateLabels(item.transform, FriendLabels);
            foreach (var item in root.GetComponentsInChildren<FriendRequestItem>(true)) ValidateLabels(item.transform, RequestLabels);
            foreach (var item in root.GetComponentsInChildren<FollowingItem>(true)) ValidateLabels(item.transform, FollowingLabels);
        }
    }

    static void ValidateLabels(Transform root, string[] paths)
    {
        foreach (string path in paths)
        {
            var text = Label(root, path);
            if (text.color != LabelColor)
                throw new InvalidOperationException("Friend action label color is not fixed: " + root.name + "/" + path);
        }
    }
}
#endif
