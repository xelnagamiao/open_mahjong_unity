using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Fixed UI lives in the scene; reusable item and notification templates stay prefabs.</summary>
public static class MainSceneUiBaker {
    [MenuItem("Tools/Mahjong/Main Scene/Bake Fixed UI")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        foreach (var panel in Find<CreatePanel>(scene)) {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake create room UI");
            panel.BakeFixedRoomUi();
            MarkHierarchy(panel.gameObject);
        }
        foreach (var panel in Find<GamePlayerPanel>(scene)) {
            panel.BakeDuplicateRemainingTilesLabel();
            panel.transform.Find("DuplicateRemainingTiles").gameObject.SetActive(false);
            MarkHierarchy(panel.gameObject);
        }
        foreach (var panel in Find<TipsContainer>(scene)) {
            panel.ApplyHintStyle();
            MarkHierarchy(panel.gameObject);
        }
        foreach (var chat in Find<ChatPanel>(scene)) {
            if (!chat.GetComponent<CanvasGroup>()) Undo.AddComponent<CanvasGroup>(chat.gameObject);
            MarkHierarchy(chat.gameObject);
        }
        foreach (var panel in Find<InventoryPanel>(scene)) panel.BakeWarehouseUi();
        foreach (var panel in Find<RoomPanel>(scene)) {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake room bot speed");
            panel.BakeBotSpeedUi();
            MarkHierarchy(panel.gameObject);
        }
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Validate Fixed UI")]
    public static void ValidateActiveScene() {
        Validate(SceneManager.GetActiveScene());
        Debug.Log("主场景固定 UI 与序列化引用检查通过。");
    }

    public static void Validate(Scene scene) {
        foreach (var panel in Find<CreatePanel>(scene)) {
            var serialized = new SerializedObject(panel);
            var property = serialized.GetIterator();
            while (property.NextVisible(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Require(property.objectReferenceValue, panel, property.propertyPath);
            foreach (var key in new[] { "taiwan", "riichi" }) {
                var overlay = panel.transform.Find("DetailedConfigPanel_" + key);
                Require(overlay && overlay.GetComponent<CreateRoomPopupMotion>(), panel, key + " 弹窗动画");
                Require(!overlay.gameObject.activeSelf, panel, key + " 弹窗初始状态");
                var scroll = overlay.Find("Dialog/Create_Panel/ScrollArea").GetComponent<ScrollRect>();
                var layout = scroll.content.GetComponent<CreateRoomDetailsLayout>();
                Require(layout && layout.Columns == (key == "riichi" ? 4 : 2), panel, key + " 布局列数");
                Require(scroll.scrollSensitivity >= 100 && layout.padding.bottom >= 72, panel, key + " 滚动设置");
            }
            panel.ValidateFixedRuleOptions();
            Require(panel.transform.Find("Create_Panel/TierPresets")?.childCount == 4, panel, "四种规则预设");
        }
        foreach (var panel in Find<SpectatorPanel>(scene)) {
            Require(!PrefabUtility.IsPartOfPrefabInstance(panel), panel, "观战页面应直接保存在主场景");
            var item = new SerializedObject(panel).FindProperty("SpectatorPrefab").objectReferenceValue;
            Require(item && PrefabUtility.IsPartOfPrefabAsset(item), panel, "观战条目预制体");
        }
        foreach (var chat in Find<ChatPanel>(scene)) {
            Require(chat.GetComponent<CanvasGroup>(), chat, "创房时的聊天显示控制");
        }
        foreach (var player in Find<GamePlayerPanel>(scene))
            Require(new SerializedObject(player).FindProperty("duplicateRemainingTilesText").objectReferenceValue, player, "复式余牌标签");
        foreach (var tips in Find<TipsContainer>(scene))
            Require(tips.transform.Find("HintFrame")?.GetComponent<TipsCornerFrame>(), tips, "听牌提示边框");
        foreach (var panel in Find<InventoryPanel>(scene)) panel.ValidateWarehouseUi();
        foreach (var panel in Find<RoomPanel>(scene)) panel.ValidateBotSpeedUi();
        SceneSettingsUiBaker.Validate(scene);
    }

    private static void Require(bool condition, Component owner, string field) {
        if (!condition) throw new InvalidOperationException(owner.name + " 固定 UI 未烘焙或引用缺失：" + field);
    }
    private static T[] Find<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void MarkHierarchy(GameObject root) {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
            if (component) EditorUtility.SetDirty(component);
    }
}

public sealed class MainSceneUiBuildCheck : IProcessSceneWithReport {
    public int callbackOrder => 1;
    public void OnProcessScene(Scene scene, BuildReport report) {
        try { MainSceneUiBaker.Validate(scene); }
        catch (Exception error) { throw new BuildFailedException(error.Message); }
    }
}
