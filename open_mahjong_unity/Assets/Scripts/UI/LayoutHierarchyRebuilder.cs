using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds a changed UI branch from the innermost RectTransform towards its
/// owning panel so parent fitters do not retain the previous child size.
/// </summary>
public static class LayoutHierarchyRebuilder {
    public static void RebuildUpwards(Transform changed, Transform boundary = null) {
        if (changed == null) return;

        var chain = new List<RectTransform>();
        Transform current = changed;
        while (current != null) {
            if (current is RectTransform rect) chain.Add(rect);
            if (current == boundary) break;
            if (boundary == null && current.GetComponent<Canvas>() != null) break;
            current = current.parent;
        }

        Canvas.ForceUpdateCanvases();
        RebuildChain(chain);
        // Fitters resize themselves after their children. The second
        // bottom-up pass propagates that new size through all parent groups.
        Canvas.ForceUpdateCanvases();
        RebuildChain(chain);
        Canvas.ForceUpdateCanvases();
    }

    private static void RebuildChain(List<RectTransform> chain) {
        for (int index = 0; index < chain.Count; index++) {
            RectTransform rect = chain[index];
            if (rect != null && rect.gameObject.activeInHierarchy) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }
    }
}
