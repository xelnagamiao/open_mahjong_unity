using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MatchReplayPhaseDropdown : TMP_Dropdown {
    protected override GameObject CreateBlocker(Canvas rootCanvas) {
        var blocker = base.CreateBlocker(rootCanvas);
        blocker.AddComponent<ToolbarRaycastFilter>().owner = this;
        return blocker;
    }

    protected override void DestroyDropdownList(GameObject dropdownList) {
        dropdownList.SetActive(false);
        base.DestroyDropdownList(dropdownList);
    }

    protected override void DestroyBlocker(GameObject blocker) {
        blocker.SetActive(false);
        base.DestroyBlocker(blocker);
    }

    private sealed class ToolbarRaycastFilter : MonoBehaviour, ICanvasRaycastFilter {
        public MatchReplayPhaseDropdown owner;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) {
            foreach (Transform child in owner.transform.parent) {
                if (child == owner.transform || !child.TryGetComponent<Selectable>(out var action)
                    || !action.IsActive() || !action.IsInteractable()) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)child, screenPoint, eventCamera)) return false;
            }
            return true;
        }
    }
}
