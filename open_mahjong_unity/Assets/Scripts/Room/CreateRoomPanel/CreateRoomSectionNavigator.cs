using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CreateRoomSectionNavigator : MonoBehaviour {
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform[] sections;
    private Coroutine motion;

    public void Configure(TMP_Dropdown selector, ScrollRect target, RectTransform[] anchors) {
        if (dropdown) dropdown.onValueChanged.RemoveListener(JumpTo);
        dropdown = selector; scroll = target; sections = anchors;
        if (isActiveAndEnabled) dropdown.onValueChanged.AddListener(JumpTo);
    }
    private void OnEnable() { if (dropdown) dropdown.onValueChanged.AddListener(JumpTo); }
    private void OnDisable() {
        if (dropdown) dropdown.onValueChanged.RemoveListener(JumpTo);
        if (motion != null) StopCoroutine(motion);
        motion = null;
    }
    public void JumpTo(int index) {
        if (!scroll || sections == null || index < 0 || index >= sections.Length || !sections[index]) return;
        Canvas.ForceUpdateCanvases();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.content, sections[index]);
        float distance = Mathf.Max(1, scroll.content.rect.height - scroll.viewport.rect.height);
        float target = index == 0 ? 1 : 1 - Mathf.Clamp01((-bounds.max.y - 8) / distance);
        scroll.StopMovement();
        if (motion != null) StopCoroutine(motion);
        if (Application.isPlaying && isActiveAndEnabled) motion = StartCoroutine(Move(target));
        else scroll.verticalNormalizedPosition = target;
    }
    private IEnumerator Move(float target) {
        float from = scroll.verticalNormalizedPosition;
        for (float elapsed = 0; elapsed < .22f; elapsed += Time.unscaledDeltaTime) {
            scroll.verticalNormalizedPosition = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, elapsed / .22f));
            yield return null;
        }
        scroll.verticalNormalizedPosition = target; motion = null;
    }
}
