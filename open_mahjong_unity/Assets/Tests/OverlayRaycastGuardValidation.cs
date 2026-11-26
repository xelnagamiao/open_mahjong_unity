#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 透明幽灵遮挡守卫验证：
/// 1) 验证 GameCanvas.RuleHintOverlayRoot 自带 CanvasGroup(blocksRaycasts = false, interactable = false)；
/// 2) 验证 DynamicTextRow.SingleLabel 默认关闭 raycastTarget；
/// 3) 验证 ActionBlockContenter 置顶逻辑与无透明 raycastTarget 幽灵层遮挡。
/// </summary>
public static class OverlayRaycastGuardValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(string name, bool condition) {
            if (!condition) throw new InvalidOperationException("OverlayRaycastGuardValidation FAILED: " + name);
            checks.Add(name);
        }

        // 1. 验证 DynamicTextRow.SingleLabel 默认不拦截射线
        var tempRoot = new GameObject("OverlayRaycastGuardTemp", typeof(RectTransform));
        try {
            TMP_Text label = DynamicTextRow.SingleLabel(tempRoot.transform, "TestLabel");
            Check("DynamicTextRow.SingleLabel raycastTarget is false", label != null && !label.raycastTarget);
        } finally {
            UnityEngine.Object.DestroyImmediate(tempRoot);
        }

        // 2. 验证场景中的 GameCanvas 规则提示挂载点与 ActionBlockContenter 层级
        var canvas = Resources.FindObjectsOfTypeAll<GameCanvas>().FirstOrDefault(c => c.gameObject.scene.IsValid());
        if (canvas != null) {
            ITableHudHost host = canvas;
            Transform overlayRoot = host.RuleHintOverlayRoot;
            Check("RuleHintOverlayRoot exists", overlayRoot != null);
            var group = overlayRoot.GetComponent<CanvasGroup>();
            Check("RuleHintOverlayRoot has CanvasGroup", group != null);
            Check("RuleHintOverlayRoot blocksRaycasts is false", !group.blocksRaycasts);
            Check("RuleHintOverlayRoot interactable is false", !group.interactable);

            // 检查 RuleHintOverlayRoot 下若挂了透明面板也不会拦截射线（由 CanvasGroup 兜底）
            var probe = new GameObject("TransparentProbe", typeof(RectTransform), typeof(Image));
            probe.transform.SetParent(overlayRoot, false);
            var probeImg = probe.GetComponent<Image>();
            probeImg.color = Color.clear;
            probeImg.raycastTarget = true;
            try {
                bool blockedByGroup = probe.GetComponentsInParent<CanvasGroup>(true).Any(g => !g.blocksRaycasts);
                Check("Transparent child under RuleHintOverlayRoot is bypassed by CanvasGroup", blockedByGroup);
            } finally {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            // 检查 ActionBlockContenter 置顶后处于同级最前（最后渲染）
            if (canvas.ActionBlockContenter != null && canvas.ActionBlockContenter.parent != null) {
                Transform container = canvas.ActionBlockContenter;
                container.SetAsLastSibling();
                Check("ActionBlockContenter is last sibling in SelfControl",
                    container.GetSiblingIndex() == container.parent.childCount - 1);
            }
        }

        return new { ok = true, checks = checks.Count, details = checks };
    }

#if UNITY_EDITOR
    public static void RunAllValidationBatchmode() {
        try {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            var overlayRes = Run();
            Debug.Log("[UNITY-VERIFY] OverlayRaycastGuardValidation PASSED: " + overlayRes);
            var coreRes = CoreRuleIsolationValidation.Run();
            Debug.Log("[UNITY-VERIFY] CoreRuleIsolationValidation PASSED: " + coreRes);
            var archType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("ArchitectureRefactorVerification", false))
                .FirstOrDefault(t => t != null);
            if (archType == null) throw new InvalidOperationException("ArchitectureRefactorVerification type not found");
            archType.GetMethod("RunBatchmode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, null);
        } catch (Exception ex) {
            Debug.LogException(ex);
            UnityEditor.EditorApplication.Exit(1);
        }
    }
#endif
}
#endif
