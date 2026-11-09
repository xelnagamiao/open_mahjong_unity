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
        InterfaceSceneCleanupBaker.Bake(scene);
        EventContentUiBaker.Bake(scene);
        PlatformTypographyBaker.RepairSceneMaterials(scene);
        foreach (var panel in Find<CreatePanel>(scene)) {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake create room UI");
            panel.BakeFixedRoomUi();
            MarkHierarchy(panel.gameObject);
        }
        foreach (var panel in Find<GamePlayerPanel>(scene)) {
            GamePlayerPanelUiBaker.Bake(panel);
            MarkHierarchy(panel.gameObject);
        }
        foreach (var panel in Find<TipsContainer>(scene)) {
            panel.ApplyHintStyle();
            MarkHierarchy(panel.gameObject);
        }
        ChatUiBaker.Bake(scene);
        foreach (var panel in Find<InventoryPanel>(scene)) panel.BakeWarehouseUi();
        foreach (var panel in Find<RoomPanel>(scene)) {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake room bot speed");
            panel.BakeBotSpeedUi();
            MarkHierarchy(panel.gameObject);
        }
        RuleSupportTableBaker.Bake(scene);
        GuobiaoFanTableUiBaker.Bake(scene);
        HomeArtworkUiBaker.Bake(scene);
        RoundPanelUiBaker.Bake(scene);
        ReplayControlsBaker.Bake(scene);
        TableHudClarityBaker.Bake(scene);
        HomepageLocalizationBaker.BakePrefabs(scene);
        HomepageLocalizationBaker.Bake(scene);
        PlatformEnglishLayoutBaker.Bake(scene);
        InterfacePlansPolishBaker.Bake(scene);
        CommunityUiBaker.ApplyFixed(scene);
        MailUiBaker.Bake(scene);
        RewardUiBaker.Bake(scene);
        ShopUiBaker.Bake(scene);
        GameEndRewardsUiBaker.Bake(scene);
        SalaCoinShopUiBaker.Bake(scene);
        MailBatchLayoutBaker.Bake(scene);
        GameNavigationUiBaker.Bake(scene);
        PlatformNavigationBaker.Bake(scene);
        LobbyIconSelectionBaker.Bake(scene);
        MatchCardLayoutBaker.Bake(scene);
        RoomMatchPageLayoutBaker.Bake(scene);
        MatchDetailUiBaker.Bake(scene);
        EventContentUiBaker.Bake(scene);
        EventVenuePresentationBaker.Bake(scene);
        VenueListPolishBaker.Bake(scene);
        InterfaceDesignPresetsBaker.Bake(scene);
        HomepageLocalizationBaker.Bake(scene);
        PlatformTypographyBaker.BakePrefabs(scene);
        PlatformTypographyBaker.Bake(scene);
        GameUiLocalizationBaker.BakePrefabs();
        GameUiLocalizationBaker.Bake(scene);
        FriendPaletteBaker.Bake(scene);
        LobbyTableTransitionBaker.Bake(scene);
        FontFallbackRepairBaker.Bake(scene);
        SceneAuthoringLayoutBaker.Bake(scene);
        foreach (var panel in Find<CreatePanel>(scene)) panel.SaveDeferredPanels();
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Mahjong/Main Scene/Validate Fixed UI")]
    public static void ValidateActiveScene() {
        Validate(SceneManager.GetActiveScene());
        Debug.Log("主场景固定 UI 与序列化引用检查通过。");
    }

    public static void Validate(Scene scene) {
        InterfaceSceneCleanupBaker.Validate(scene);
        MainCanvasPresentationValidation.Validate(scene);
        DataPageUiBaker.Validate(scene);
        RoomMatchPageLayoutBaker.Validate(scene);
        MatchDetailUiBaker.Validate(scene);
        ReplayControlsBaker.Validate(scene);
        EventContentUiBaker.Validate(scene);
        EventVenuePresentationBaker.Validate(scene);
        VenueListPolishBaker.Validate(scene);
        InterfaceDesignPresetsBaker.Validate(scene);
        FriendPaletteBaker.Validate(scene);
        EventVenueTypographyBaker.Validate(scene);
        FreeModeTypographyBaker.Validate();
        PlayerDataIconBaker.Validate(scene);
        HomeArtworkUiBaker.Validate(scene);
        GuobiaoFanTableUiBaker.Validate(scene);
        RuleSupportTableBaker.Validate(scene);
        foreach (var panel in Find<CreatePanel>(scene)) {
            var serialized = new SerializedObject(panel);
            var property = serialized.GetIterator();
            while (property.NextVisible(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Require(property.objectReferenceValue, panel, property.propertyPath);
            panel.ValidateDeferredPanels();
            panel.ValidateFixedRuleOptions();
            Require(panel.transform.Find("Create_Panel/TierPresets")?.childCount >= 5, panel, "规则预设（含雀庄公式战）");
        }
        foreach (var panel in Find<SpectatorPanel>(scene)) {
            SpectatorUiBaker.Validate(panel);
            Require(!PrefabUtility.IsPartOfPrefabInstance(panel), panel, "观战页面应直接保存在主场景");
            var item = new SerializedObject(panel).FindProperty("SpectatorPrefab").objectReferenceValue;
            Require(item && PrefabUtility.IsPartOfPrefabAsset(item), panel, "观战条目预制体");
        }
        ChatUiBaker.Validate(scene);
        RewardUiBaker.Validate(scene);
        ShopUiBaker.Validate(scene);
        GameEndRewardsUiBaker.Validate(scene);
        SalaCoinShopUiBaker.Validate(scene);
        foreach (var player in Find<GamePlayerPanel>(scene))
            GamePlayerPanelUiBaker.Validate(player);
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
        // Play Mode has already initialized the UI; authored inactive-state checks apply to player builds.
        if (report == null || scene.path == "Assets/AndroidUpdate/AndroidBootstrap.unity") return;
        try { RuleSupportTableBaker.Bake(scene); MainSceneUiBaker.Validate(scene); }
        catch (Exception error) { throw new BuildFailedException(error.Message); }
    }
}
