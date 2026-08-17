using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 用 Editor API 在已打开的 MainScene 里搭建通知活动面板，不直接改 YAML。
/// </summary>
public static class NoticePanelBaker {
    [MenuItem("Tools/Notice/搭建通知活动面板")]
    public static void Build() {
        GameObject notice = FindInOpenScenes("NoticePanel");
        if (notice == null) {
            EditorUtility.DisplayDialog("通知面板", "当前打开的场景里找不到 NoticePanel。请先打开 MainScene。", "好的");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(notice, "搭建通知活动面板");
        NoticePanel panel = notice.GetComponent<NoticePanel>();
        if (panel == null) panel = Undo.AddComponent<NoticePanel>(notice);
        panel.EnsureUi();
        EditorUtility.SetDirty(notice);
        EditorUtility.SetDirty(panel);
        if (panel.gameObject.scene.IsValid()) {
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        }
        Selection.activeGameObject = notice;
        EditorUtility.DisplayDialog(
            "通知面板",
            "已在 NoticePanel 下生成滚动列表、活动卡片和详情窗。请保存场景。\n编辑器播放时从 http://localhost:3000/activity-assets/index.json 拉取活动。",
            "好的"
        );
    }

    private static GameObject FindInOpenScenes(string name) {
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

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform nested = FindDeep(root.GetChild(i), name);
            if (nested != null) return nested;
        }
        return null;
    }
}
