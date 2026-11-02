using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HeaderUtilityDismissFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        public FourSideHeader navigation;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!navigation || !navigation.UtilityOpen) return true;
            if (Contains(navigation.main, screenPoint, eventCamera) ||
                Contains(navigation.more, screenPoint, eventCamera)) return false;
            foreach (var button in navigation.tabs)
                if (Contains(button, screenPoint, eventCamera)) return false;
            return true;
        }

        static bool Contains(FourSideHeaderButton target, Vector2 point, Camera camera)
        {
            return target && target.gameObject.activeInHierarchy && target.button &&
                target.button.IsActive() && target.button.IsInteractable() &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)target.transform, point, camera);
        }
    }
}
