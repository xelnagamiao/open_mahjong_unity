#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed partial class InventoryPanel
{
    public void BakeWarehouseCategories()
    {
        if (Application.isPlaying) throw new InvalidOperationException("仓库分类只能在编辑模式下烘焙。");
        if (categoryButtons == null || categoryButtons.Length < 4 || !categoryButtons[3])
            throw new InvalidOperationException("仓库原有分类引用缺失。");
        var layout = GetComponent<InventoryLayoutVariants>();
        bool compact = layout && layout.VariantCount == 1;
        if (compact) layout.SelectVariant(0);
        Array.Resize(ref categoryButtons, CategoryNames.Length);
        var avatar = transform.Find("Category_4");
        if (!avatar)
        {
            avatar = Instantiate(categoryButtons[3].gameObject, transform, false).transform;
            avatar.name = "Category_4";
            Undo.RegisterCreatedObjectUndo(avatar.gameObject, "Add inventory avatar category");
        }
        categoryButtons[4] = avatar.GetComponent<UnityEngine.UI.Button>();
        if (!categoryButtons[4]) throw new InvalidOperationException("头像分类缺少按钮组件。");
        var avatarLabel = avatar.GetComponentInChildren<TMP_Text>(true);
        var sourceLabel = categoryButtons[3].GetComponentInChildren<TMP_Text>(true);
        avatarLabel.font = sourceLabel.font; avatarLabel.fontSharedMaterial = sourceLabel.fontSharedMaterial;
        avatarLabel.fontSize = sourceLabel.fontSize; avatarLabel.alignment = sourceLabel.alignment;
        var sourceFill = categoryButtons[3].targetGraphic as UnityEngine.UI.Image;
        var avatarFill = categoryButtons[4].targetGraphic as UnityEngine.UI.Image;
        if (sourceFill && avatarFill) { avatarFill.sprite=sourceFill.sprite; avatarFill.type=sourceFill.type; avatarFill.material=sourceFill.material; }
        categoryButtons[4].colors=categoryButtons[3].colors; categoryButtons[4].transition=categoryButtons[3].transition;
        var sourceLocalization=sourceLabel.GetComponent<LocalizedPlatformLabel>();
        if (sourceLocalization)
        {
            var localized=avatarLabel.GetComponent<LocalizedPlatformLabel>() ?? Undo.AddComponent<LocalizedPlatformLabel>(avatarLabel.gameObject);
            EditorUtility.CopySerialized(sourceLocalization,localized);
        }
        var sourceTypography=sourceLabel.GetComponent<PlatformFontBinding>();
        if (sourceTypography)
        {
            var typography=avatarLabel.GetComponent<PlatformFontBinding>() ?? Undo.AddComponent<PlatformFontBinding>(avatarLabel.gameObject);
            EditorUtility.CopySerialized(sourceTypography,typography);
        }
        var origin = (RectTransform)categoryButtons[0].transform;
        float spacing = 60;
        var poses = compact ? new List<InventoryLayoutVariants.RectPose>(layout.BakedVariants[0].rects) : null;
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            var button = categoryButtons[i]; var rect = (RectTransform)button.transform;
            int visualIndex = i == (int)WarehouseCategory.Avatar ? 3 : i == (int)WarehouseCategory.Cosmetic ? 4 : i;
            rect.anchorMin = origin.anchorMin; rect.anchorMax = origin.anchorMax; rect.pivot = origin.pivot;
            WarehousePosition(rect, 1, 69 + visualIndex * spacing, compact ? 152 : 180, 59);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            label.text = CategoryNames[i]; label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
            var localized = label.GetComponent<LocalizedPlatformLabel>(); if (localized) { localized.key = CategoryNames[i]; localized.SetSource(CategoryNames[i]); }
            var typography = label.GetComponent<PlatformFontBinding>(); if (typography) { typography.authoredStyle = FontStyles.Normal; typography.authoredWeight = FontWeight.Regular; EditorUtility.SetDirty(typography); }
            foreach (string childName in new[] { "CategoryIcon", "SelectedMark" })
            {
                var decoration = button.transform.Find(childName);
                if (decoration) Undo.DestroyObjectImmediate(decoration.gameObject);
            }
            button.gameObject.SetActive(true);
            WarehousePosition(label.rectTransform, 14, 0, rect.sizeDelta.x - 28, rect.sizeDelta.y);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var fill = button.targetGraphic as UnityEngine.UI.Image;
            if (fill) { WarehouseRounded(fill); fill.raycastTarget = true; }
            WarehouseButtonColors(button);
            if (compact) { CaptureWarehousePose(poses, rect); CaptureWarehousePose(poses, label.rectTransform); }
            foreach (var component in button.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        }
        if (compact) { layout.BakedVariants[0].rects = poses.FindAll(pose => pose.target).ToArray(); EditorUtility.SetDirty(layout); layout.SelectVariant(0); }
        EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    static void CaptureWarehousePose(List<InventoryLayoutVariants.RectPose> poses,RectTransform rect)
    {
        var pose = new InventoryLayoutVariants.RectPose { target=rect, anchorMin=rect.anchorMin, anchorMax=rect.anchorMax,
            pivot=rect.pivot, position=rect.anchoredPosition, size=rect.sizeDelta, scale=rect.localScale };
        int index=poses.FindIndex(item=>item.target==rect);
        if (index>=0) poses[index]=pose; else poses.Add(pose);
    }
}
#endif
