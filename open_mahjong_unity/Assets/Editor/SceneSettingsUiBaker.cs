using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Author fixed settings UI once. Player code only binds the saved references.</summary>
public static class SceneSettingsUiBaker
{
    public const string MainScenePath = "Assets/Scenes/MainScene.unity";

    [MenuItem("Tools/Mahjong/Scene Settings/Bake Fixed UI")]
    public static void BakeActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式，再烘焙场景设置。");
        var scene = SceneManager.GetActiveScene();
        Bake(scene);
        Debug.Log("场景设置固定 UI 已烘焙，请保存场景。再次执行不会重复添加控件。");
    }

    public static int Bake(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("需要已加载的编辑场景。");
        var panels = Find<SceneConfigPanel>(scene);
        if (panels.Length == 0) throw new InvalidOperationException("场景没有 SceneConfigPanel。");
        int before = panels.Sum(p => p.GetComponentsInChildren<Transform>(true).Length);
        foreach (var panel in panels)
        {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake scene settings UI");
            foreach (var cards in panel.GetComponentsInChildren<Card3DPresetPanel>(true)) cards.UpgradeLayout();
            foreach (var color in panel.GetComponentsInChildren<TableSurfaceColorEditor>(true)) color.BakePalette();
            panel.BakeFixedUi();
            panel.BakeButtonFeedback();
            foreach (var center in panel.GetComponentsInChildren<CenterDisplayConfigPanel>(true)) center.BakeLayout();
            foreach (var back in panel.GetComponentsInChildren<CardBackConfigPanel>(true)) back.BakePreviewLayout();
            foreach (var background in panel.GetComponentsInChildren<CardFaceBackgroundPanel>(true)) background.BakePreviewLayout();
            foreach (var slot in panel.GetComponentsInChildren<CardFacePreviewSlot>(true)) slot.BakePreviewLayout();
            // Older serialized headers and explanatory text are no longer runtime fallback UI.
            RemoveRetiredAnnotations(panel);
            foreach (var component in panel.GetComponentsInChildren<Component>(true))
                if (component != null) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(panel.gameObject);
        }
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        return panels.Sum(p => p.GetComponentsInChildren<Transform>(true).Length) - before;
    }

    public static void Validate(Scene scene)
    {
        foreach (var root in Find<SceneConfigPanel>(scene))
        {
            Require(root.HasBakedFixedUi, root, "固定按钮或卡牌预设入口");
            foreach (var surface in root.GetComponentsInChildren<TableSurfacePanel>(true))
            {
                var color = surface.GetComponent<TableSurfaceColorEditor>();
                Require(color != null && color.HasBakedUi, surface, "底色下拉与调色盘");
                if (surface.IsClothSurface) {
                    var seams = surface.GetComponent<TableSeamSelector>();
                    Require(seams != null && seams.HasBakedUi, surface, "桌布 header");
                } else {
                    var frame = surface.GetComponent<TableFrameHeader>();
                    Require(frame != null && frame.HasBakedUi, surface, "边框 header");
                }
                Require(surface.transform.Find("NewSurfaceColorButton") != null, surface, "新建颜色按钮");
            }
            foreach (var cards in root.GetComponentsInChildren<Card3DPresetPanel>(true))
                Require(cards.HasBakedUi, cards, "卡牌预设、轮转与新建页");
            foreach (var center in root.GetComponentsInChildren<CenterDisplayConfigPanel>(true))
                Require(center.HasBakedUi, center, "中心盘固定目录");
            foreach (var back in root.GetComponentsInChildren<CardBackConfigPanel>(true))
                Require(HasReference(back, "previewArtwork"), back, "牌背预览图层");
            foreach (var background in root.GetComponentsInChildren<CardFaceBackgroundPanel>(true)) {
                Require(HasReference(background, "tableBgArtwork"), background, "牌面背景预览图层");
                Require(HasReference(background, "handBgStyles") && HasReference(background, "handBackStyles"),
                    background, "手牌背景与牌背选择器");
                Require(HasReference(background, "handLayoutButton"), background, "牌面位置入口");
                Require(HasReference(background, "handBackFollowButton"), background, "手牌牌背自动跟随入口");
            }
            foreach (var slot in root.GetComponentsInChildren<CardFacePreviewSlot>(true))
                Require(HasReference(slot, "tableBackgroundLayer"), slot, "牌面背景层");
            foreach (var palette in root.GetComponentsInChildren<TableSurfaceColorEditor>(true))
                Require(!Reference(palette, "modal").GameObject().activeSelf, palette, "调色盘初始应关闭");
        }
    }

    static UnityEngine.Object Reference(UnityEngine.Object target, string name)
        => new SerializedObject(target).FindProperty(name)?.objectReferenceValue;
    static bool HasReference(UnityEngine.Object target, string name) => Reference(target, name) != null;
    static GameObject GameObject(this UnityEngine.Object value) => value is Component c ? c.gameObject : value as GameObject;
    static void Require(bool condition, Component component, string label)
    {
        if (!condition) throw new InvalidOperationException(component.name + " 尚未烘焙或引用不完整：" + label);
    }
    static T[] Find<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    static void RemoveRetiredAnnotations(SceneConfigPanel root)
    {
        foreach (var surface in root.GetComponentsInChildren<TableSurfacePanel>(true))
        {
            var obsolete = new List<GameObject>();
            foreach (Transform child in surface.transform) {
                var text = child.GetComponent<TMPro.TMP_Text>();
                if (text != null && !child.gameObject.activeSelf && (text.text == "" || text.text.Contains("PlayerPrefs")))
                    obsolete.Add(child.gameObject);
            }
            foreach (var item in obsolete) Undo.DestroyObjectImmediate(item);
        }
        foreach (var center in root.GetComponentsInChildren<CenterDisplayConfigPanel>(true))
            foreach (string name in new[] { "Description", "StatusText", "HelpText" }) {
                var item = center.transform.Find(name);
                if (item != null) Undo.DestroyObjectImmediate(item.gameObject);
            }
    }
}

/// <summary>Fail a build explicitly if somebody adds a scene but forgets its fixed UI.</summary>
public sealed class SceneSettingsUiBuildCheck : IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        try { SceneSettingsUiBaker.Validate(scene); }
        catch (Exception error) { throw new BuildFailedException(error.Message); }
    }
}
