using System.Collections.Generic;
using UnityEngine;

public partial class GameSceneMouseInputController {
    /// <summary>Register a runtime UI canvas without replacing the authored shortcut exclusions.</summary>
    public void RegisterAdditionalUiRoot(RectTransform root) {
        if (!root) return;
        var roots = new List<RectTransform>(additionalExcludeRects ?? System.Array.Empty<RectTransform>());
        if (roots.Contains(root)) return;
        roots.Add(root); additionalExcludeRects = roots.ToArray();
    }

    public void UnregisterAdditionalUiRoot(RectTransform root) {
        if (!root || additionalExcludeRects == null) return;
        var roots = new List<RectTransform>(additionalExcludeRects);
        roots.Remove(root); additionalExcludeRects = roots.ToArray();
    }
}
