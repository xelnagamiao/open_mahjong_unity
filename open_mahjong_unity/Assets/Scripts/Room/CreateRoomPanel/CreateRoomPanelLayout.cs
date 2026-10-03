using UnityEngine;

[ExecuteAlways]
public sealed class CreateRoomPanelLayout : MonoBehaviour {
    [SerializeField] private CreatePanel owner;
    private Vector2 size;
    public void Configure(CreatePanel panel) { owner = panel; size = Vector2.zero; }
    private void LateUpdate() {
        if (!owner) return;
        Vector2 next = ((RectTransform)transform).rect.size;
        if (next == size) return;
        size = next; owner.ReflowRoomPresentation();
    }
}
