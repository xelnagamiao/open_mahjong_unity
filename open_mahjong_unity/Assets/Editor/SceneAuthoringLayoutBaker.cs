#if UNITY_EDITOR
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persists viewport-dependent geometry without running page logic or issuing network requests.</summary>
public static class SceneAuthoringLayoutBaker {
    public static void Bake(Scene scene) {
        var roots = scene.GetRootGameObjects();
        Canvas.ForceUpdateCanvases();
        foreach (var layout in roots.SelectMany(r => r.GetComponentsInChildren<CanvasReferenceLayout>(true)).OrderBy(l => Depth(l.transform)))
            layout.Apply(true);
        foreach (var layout in roots.SelectMany(r => r.GetComponentsInChildren<FullscreenPageLayout>(true))) {
            layout.Apply(true);
            var view = layout.GetComponentInChildren<MatchLobbyView>(true);
            if (view) {
                foreach (var group in view.GetComponentsInChildren<UnityEngine.UI.LayoutGroup>(true))
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                foreach (var content in view.GetComponentsInChildren<MatchContentLayout>(true)) content.Apply(true);
            }
        }
        DataPageUiBaker.Bake(scene);
        foreach (var layout in roots.SelectMany(r => r.GetComponentsInChildren<CanvasReferenceLayout>(true))) {
            EditorUtility.SetDirty(layout.transform); EditorUtility.SetDirty(layout);
        }
    }
    static int Depth(Transform node) { int depth = 0; while(node.parent) { node=node.parent; depth++; } return depth; }
}
#endif
