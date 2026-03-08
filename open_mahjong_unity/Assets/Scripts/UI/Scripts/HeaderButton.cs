using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 导航栏单个按钮：管理图标 Image 的常态/选中/悬停/按下颜色与按下·松开渐变，不依赖 Panel 的全局颜色。
/// Panel 仅通过 SetState 传入“是否选中”和“Stay 颜色”（如房间常驻色）。
/// </summary>
public class HeaderButton : MonoBehaviour {
    [SerializeField] private Button button;
    [SerializeField] private Image image;

    [Header("按钮自身颜色")]
    [Tooltip("未选中时的颜色")]
    [SerializeField] private Color normalColor = Color.white;
    [Tooltip("当前选中时的颜色")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.6f);
    [Tooltip("悬停时的颜色")]
    [SerializeField] private Color hoverColor = new Color(0.95f, 0.95f, 1f);
    [Tooltip("按下时的颜色")]
    [SerializeField] private Color pressedColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("渐变时长（秒）")]
    [SerializeField] private float pressLerpDuration = 0.06f;
    [SerializeField] private float releaseLerpDuration = 0.1f;

    private Color _restColor;
    private Color _hoverColor;
    private Coroutine _tween;

    public Button Button => button;

    private void Awake() {
        if (button != null) button.transition = Selectable.Transition.None;
    }

    private void Start() {
        RegisterEvents();
    }

    /// <summary>
    /// 由 HeaderPanel 调用。isSelected=当前是否为选中页；isStay=是否处于“常驻”态（如房间按钮在房间里但未选房间页）；stayColor=常驻态使用的颜色，由 Panel 设置。
    /// 松开时也使用渐变过渡到新的状态色。
    /// </summary>
    public void SetState(bool isSelected, bool isStay, Color stayColor) {
        _restColor = isSelected ? selectedColor : (isStay ? stayColor : normalColor);
        _hoverColor = isSelected ? selectedColor : hoverColor;
        LerpTo(_restColor, releaseLerpDuration);
    }

    private void RegisterEvents() {
        if (button == null || image == null) return;
        var et = button.GetComponent<EventTrigger>() ?? button.gameObject.AddComponent<EventTrigger>();

        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => LerpTo(pressedColor, pressLerpDuration));
        et.triggers.Add(down);

        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => LerpTo(_restColor, releaseLerpDuration));
        et.triggers.Add(up);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => LerpTo(_restColor, releaseLerpDuration));
        et.triggers.Add(exit);

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => LerpTo(_hoverColor, releaseLerpDuration * 0.5f));
        et.triggers.Add(enter);
    }

    private void LerpTo(Color target, float duration) {
        if (image == null) return;
        if (duration <= 0f) {
            image.color = target;
            StopTween();
            return;
        }
        StopTween();
        _tween = StartCoroutine(LerpRoutine(target, duration));
    }

    private IEnumerator LerpRoutine(Color target, float duration) {
        Color start = image.color;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            image.color = Color.Lerp(start, target, t);
            yield return null;
        }
        image.color = target;
        _tween = null;
    }

    private void StopTween() {
        if (_tween != null) {
            StopCoroutine(_tween);
            _tween = null;
        }
    }

    /// <summary> Panel 在刷新时调用，停止当前渐变并立即应用当前状态色（避免切页时残留动画）。 </summary>
    public void RefreshImmediate() {
        StopTween();
        if (image != null) image.color = _restColor;
    }
}
