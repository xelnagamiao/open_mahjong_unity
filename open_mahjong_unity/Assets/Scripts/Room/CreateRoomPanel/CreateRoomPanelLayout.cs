using UnityEngine;

[ExecuteAlways, DefaultExecutionOrder(22000)]
public sealed class CreateRoomPanelLayout : MonoBehaviour {
    [SerializeField] private CreatePanel owner;
    private Vector2 size;
    private AppLanguage language;
    private void OnEnable() { PlatformTypography.Changed += Invalidate; Invalidate(); }
    private void OnDisable() { PlatformTypography.Changed -= Invalidate; }
    private void Invalidate() { size = Vector2.zero; }
    public void Configure(CreatePanel panel) { owner = panel; size = Vector2.zero; }
    private void LateUpdate() {
        if (!owner) return;
        Vector2 next = ((RectTransform)transform).rect.size;
        if (next == size && language == ConfigManager.CurrentLanguage) return;
        size = next; language = ConfigManager.CurrentLanguage;
        owner.ReflowRoomPresentation();
    }
}
