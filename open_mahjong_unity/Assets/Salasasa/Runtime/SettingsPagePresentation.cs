using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed partial class SettingsPagePresentation : MonoBehaviour
    {
        public SettingsBakedPage page;
        public ScrollRect scroll;
        public RectTransform footer;
        public TMP_Text helpText;
        public SettingsStyleCollection settings;
        public Button resetDefaults;
        public SettingsHintTarget[] targets;

        public string CurrentHint => helpText ? helpText.text : string.Empty;
        public const float HintFadeDuration = .36f;
        public float HintOpacity { get; private set; }
        SettingsHintTarget pointerTarget, focusTarget, renderedTarget;
        GameObject selected;
        Vector2 pointerPosition;
        Vector2 hintPosition;
        float hintElapsed;
        int hintStartFrame;
        bool keyboardMode;
        readonly Vector3[] corners = new Vector3[4];

        void Awake()
        {
            if (helpText) hintPosition = helpText.rectTransform.anchoredPosition;
            ApplyHintStyle(settings ? settings.HintStyle : 0);
        }

        void OnEnable()
        {
            ClearHint();
            if (resetDefaults) resetDefaults.onClick.AddListener(RestoreDefaults);
            selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            pointerPosition = Input.mousePosition;
            if (scroll && scroll.content)
            {
                scroll.StopMovement();
                scroll.content.anchoredPosition = Vector2.zero;
            }
        }

        void OnDisable()
        {
            if (resetDefaults) resetDefaults.onClick.RemoveListener(RestoreDefaults);
            ClearHint();
        }

        void RestoreDefaults()
        {
            if (settings) settings.RestoreCurrentPageDefaults();
        }

        public void ClearHint()
        {
            pointerTarget = focusTarget = renderedTarget = null;
            keyboardMode = false;
            SetHint(string.Empty);
        }

        void LateUpdate()
        {
            var now = (Vector2)Input.mousePosition;
            if ((now - pointerPosition).sqrMagnitude > .1f)
            {
                pointerPosition = now;
                keyboardMode = false;
            }
            var current = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            bool navigation = Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.LeftArrow)
                || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow)
                || Input.GetKeyDown(KeyCode.DownArrow);
            if (selected != current || navigation)
            {
                selected = current;
                focusTarget = current ? current.GetComponentInParent<SettingsHintTarget>() : null;
                if (focusTarget && focusTarget.owner != this) focusTarget = null;
                keyboardMode = !Input.GetMouseButton(0);
                if (keyboardMode && focusTarget) EnsureVisible(focusTarget.Rect);
            }
            RenderHint();
            FadeHint();
        }

        public void PointerEnter(SettingsHintTarget target)
        {
            if (!isActiveAndEnabled || !target || target.owner != this) return;
            pointerTarget = target;
            keyboardMode = false;
            RenderHint();
        }

        public void PointerExit(SettingsHintTarget target)
        {
            if (pointerTarget != target) return;
            pointerTarget = null;
            if (((Vector2)Input.mousePosition - pointerPosition).sqrMagnitude > .1f) keyboardMode = false;
            RenderHint();
        }

        void RenderHint()
        {
            var target = keyboardMode ? focusTarget : pointerTarget;
            if (target && !target.isActiveAndEnabled) target = null;
            string value = target ? PlatformText.Get(target.Message) : string.Empty;
            if (helpText && (renderedTarget != target || helpText.text != value))
            {
                renderedTarget = target;
                SetHint(value);
            }
        }

        void SetHint(string value)
        {
            if (!helpText) return;
            helpText.text = value;
            helpText.rectTransform.anchoredPosition = hintPosition;
            RestartHintBackdrop();
        }

        void RestartHintBackdrop()
        {
            hintElapsed = 0;
            HintOpacity = 0;
            if (helpText) helpText.alpha = 0;
            hintStartFrame = Time.frameCount;
            MeasureHintSurface();
            RefreshHintSurface(0);
        }

        void FadeHint()
        {
            if (!helpText || string.IsNullOrEmpty(helpText.text) || hintElapsed >= HintFadeDuration || Time.frameCount == hintStartFrame) return;
            hintElapsed = Mathf.Min(HintFadeDuration, hintElapsed + Time.unscaledDeltaTime);
            float t = hintElapsed / HintFadeDuration;
            float eased = 1 - (1 - t) * (1 - t);
            HintOpacity = eased;
            helpText.alpha = eased;
            RefreshHintSurface(eased);
        }

        public void EnsureVisible(RectTransform target)
        {
            if (!target || !scroll || !scroll.viewport || !scroll.content) return;
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow <= 0) return;
            target.GetWorldCorners(corners);
            float bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
            float top = scroll.viewport.InverseTransformPoint(corners[1]).y;
            float delta = top > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - top
                : bottom < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bottom : 0;
            if (Mathf.Abs(delta) < .01f) return;
            scroll.StopMovement();
            var position = scroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + delta, 0, overflow);
            scroll.content.anchoredPosition = position;
        }
    }
}
