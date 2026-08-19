using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 用 Editor API 把信息/屏蔽菜单挂到四个玩家面板，并用 SerializedObject 写入 Inspector 引用（相当于拖拽赋值）。
/// </summary>
public static class PlayerActionMenuBaker {
    const string VisibleSpritePath = "Assets/Resources/Icon/iconmonstr.com/iconmonstr-x-mark-square-lined-240.png";
    const string MutedSpritePath = "Assets/Resources/Icon/iconmonstr.com/iconmonstr-x-mark-square-filled-240.png";

    static readonly string[] PanelNames = {
        "#SelfPlayer",
        "#LeftPlayer",
        "#TopPlayer",
        "#RightPlayer",
    };

    [MenuItem("Tools/Game/绘制玩家面板操作菜单")]
    public static void Build() {
        int count = Bake(recreate: true);
        ShowResult(count, true);
    }

    [MenuItem("Tools/Game/绑定玩家操作菜单引用")]
    public static void BindOnly() {
        int count = Bake(recreate: false);
        ShowResult(count, false);
    }

    static int Bake(bool recreate) {
        int count = 0;
        Scene scene = default;
        for (int i = 0; i < PanelNames.Length; i++) {
            GameObject panelGo = FindInOpenScenes(PanelNames[i]);
            if (panelGo == null) continue;
            GamePlayerPanel panel = panelGo.GetComponent<GamePlayerPanel>();
            if (panel == null) continue;

            Undo.RegisterFullObjectHierarchyUndo(panelGo, recreate ? "绘制玩家面板操作菜单" : "绑定玩家操作菜单引用");
            if (recreate) {
                Transform old = panelGo.transform.Find(GamePlayerPanel.ActionMenuName);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                panel.EnsureActionMenu();
                Transform created = panelGo.transform.Find(GamePlayerPanel.ActionMenuName);
                if (created != null) Undo.RegisterCreatedObjectUndo(created.gameObject, "绘制玩家面板操作菜单");
            }

            if (!BindSerializedReferences(panel)) continue;
            if (panelGo.scene.IsValid()) scene = panelGo.scene;
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(panelGo);
            count++;
        }

        if (count > 0 && scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        return count;
    }

    static bool BindSerializedReferences(GamePlayerPanel panel) {
        Transform root = panel.transform.Find(GamePlayerPanel.ActionMenuName);
        if (root == null) return false;

        Transform info = root.Find("InfoButton");
        Transform mute = root.Find("MuteButton");
        Button infoButton = info != null ? info.GetComponent<Button>() : null;
        Button muteButton = mute != null ? mute.GetComponent<Button>() : null;
        Image muteIcon = mute != null ? mute.Find("Icon")?.GetComponent<Image>() : null;
        TMP_Text muteLabel = mute != null ? mute.Find("Label")?.GetComponent<TMP_Text>() : null;
        Sprite visible = AssetDatabase.LoadAssetAtPath<Sprite>(VisibleSpritePath);
        Sprite muted = AssetDatabase.LoadAssetAtPath<Sprite>(MutedSpritePath);

        SerializedObject so = new SerializedObject(panel);
        so.Update();
        so.FindProperty("actionMenu").objectReferenceValue = root.gameObject;
        so.FindProperty("infoButton").objectReferenceValue = infoButton;
        so.FindProperty("muteButton").objectReferenceValue = muteButton;
        so.FindProperty("muteButtonImage").objectReferenceValue = muteIcon;
        so.FindProperty("muteButtonLabel").objectReferenceValue = muteLabel;
        so.FindProperty("stickerVisibleSprite").objectReferenceValue = visible;
        so.FindProperty("stickerMutedSprite").objectReferenceValue = muted;
        so.ApplyModifiedProperties();
        return true;
    }

    static void ShowResult(int count, bool recreated) {
        if (count == 0) {
            EditorUtility.DisplayDialog(
                "玩家操作菜单",
                recreated
                    ? "当前打开的场景里找不到四个玩家面板。请先打开 MainScene。"
                    : "找不到 PlayerActionMenu。请先执行「Tools/Game/绘制玩家面板操作菜单」。",
                "好的");
            return;
        }

        EditorUtility.DisplayDialog(
            "玩家操作菜单",
            recreated
                ? "已重新绘制菜单（方框线X=屏蔽表情，实心X=已屏蔽）。对家菜单在头像左侧。请保存场景。"
                : "已绑定引用。对家菜单在头像左侧；图标为通用方框X。请保存场景。",
            "好的");
    }

    static GameObject FindInOpenScenes(string name) {
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++) {
                Transform found = FindDeep(roots[r].transform, name);
                if (found != null) return found.gameObject;
            }
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform nested = FindDeep(root.GetChild(i), name);
            if (nested != null) return nested;
        }
        return null;
    }
}
