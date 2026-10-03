using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CreateRoomHelpLifetime : MonoBehaviour {
    [SerializeField] private CreatePanel owner;
    private readonly List<RaycastResult> hits = new List<RaycastResult>(16);
    private PointerEventData press;
    private EventSystem eventSystem;
    private int shownFrame;

#if UNITY_EDITOR
    public void Configure(CreatePanel panel) { owner = panel; }
#endif

    public void MarkShown() { shownFrame = Time.frameCount; }

    private void LateUpdate() {
        if (owner && !owner.IsRoomHelpNavigationOpen) DismissOnOutsidePress();
    }

    private bool DismissOnOutsidePress() {
        if (shownFrame == Time.frameCount || !EventSystem.current || !EventSystem.current.currentInputModule) return false;
        var input = EventSystem.current.currentInputModule.input;
        if (input.touchSupported && input.touchCount > 0) {
            for (int i = 0; i < input.touchCount; i++) {
                var touch = input.GetTouch(i);
                if (touch.phase == TouchPhase.Began && DismissAt(touch.position, touch.fingerId)) return true;
            }
            return false;
        }
        return input.GetMouseButtonDown(0) && DismissAt(input.mousePosition, -1);
    }

    private bool DismissAt(Vector2 position, int pointerId) {
        if (eventSystem != EventSystem.current) {
            eventSystem = EventSystem.current;
            press = new PointerEventData(eventSystem);
        }
        press.Reset(); press.position = position; press.pointerId = pointerId;
        hits.Clear();
        eventSystem.RaycastAll(press, hits);
        if (hits.Count > 0) {
            var target = hits[0].gameObject.transform;
            if (target.IsChildOf(transform)) return false;
            var trigger = target.GetComponentInParent<CreateRoomHelpTrigger>();
            if (trigger && trigger.IsOwnedBy(owner)) return false;
            if (target.GetComponentInParent<UnityEngine.UI.Selectable>()) return false;
        }
        owner.ShowRoomHelp("");
        return true;
    }
}
