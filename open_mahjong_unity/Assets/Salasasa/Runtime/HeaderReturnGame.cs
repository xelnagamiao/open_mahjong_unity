using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Salasasa.Home
{
    /// <summary>Resume the suspended table and highlight pending player actions.</summary>
    public sealed class HeaderReturnGame : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Button button;
        public Image icon;
        public TMP_Text caption, outline, captionShadow;
        public HeaderPresentationProfile profile;
        public RectTransform artwork;
        public HeaderReturnGraphic graphic, shadow;
        [SerializeField, Range(0, 3)] int design;
        [SerializeField] Color idleColor = new Color(.92f,.34f,.34f);
        [SerializeField] Color turnFlashColorA = Color.red;
        [SerializeField] Color turnFlashColorB = new Color(1f,.7251713f,.5235849f);
        [SerializeField] float turnFlashCycleSeconds = 1f;
        FourSideHeader navigation;
        bool hover, pressed;
        float scale = 1;
        public int DesignIndex { get; private set; }
        public bool MenuOpen => false;
        public HeaderEntryPresentation Entry => profile ? profile.Find("returnTable") : null;
        public float DesiredWidth => Entry != null && caption
            ? Mathf.Clamp(HeaderButtonPresentation.MeasureCaption(caption, Entry, profile) + 16, Entry.minimumWidth, Entry.maximumWidth) : 88;

        void Awake()
        {
            navigation = GetComponentInParent<FourSideHeader>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Resume);
            SelectDesign(design);
        }
        void OnEnable() { ConfigManager.OnLanguageChanged += RefreshLanguage; ApplyPresentation(); }
        void OnDisable() { ConfigManager.OnLanguageChanged -= RefreshLanguage; hover = pressed = false; Refresh(); }
        void OnDestroy() { if (button) button.onClick.RemoveListener(Resume); }
        void RefreshLanguage() { ApplyPresentation(); if (navigation) navigation.RequestLayout(); }
        void Resume()
        {
            if (WindowsManager.Instance && WindowsManager.Instance.CanReturnToGame)
                WindowsManager.Instance.ResumeSuspendedGame();
        }
        public bool CloseMenu() => false;
        public void SelectDesign(int index)
        {
            design = DesignIndex = Mathf.Clamp(index, 0, 3);
            if (graphic) { graphic.symbol = (HeaderReturnGraphic.Symbol)DesignIndex; graphic.SetVerticesDirty(); }
            if (shadow) { shadow.symbol = (HeaderReturnGraphic.Symbol)DesignIndex; shadow.SetVerticesDirty(); }
        }
        public void ApplyPresentation()
        {
            var entry = Entry;
            if (entry == null || !artwork) return;
            float width = ((RectTransform)transform).rect.width;
            artwork.sizeDelta = new Vector2(width, 64);
            artwork.anchoredPosition = entry.artworkOffset;
            HeaderButtonPresentation.ApplyCaption(caption, entry, profile, Vector2.zero, profile.faceMaterial, width);
            HeaderButtonPresentation.ApplyCaption(outline, entry, profile, Vector2.zero, profile.outlineMaterial, width);
            HeaderButtonPresentation.ApplyCaption(captionShadow, entry, profile, entry.shadowOffset, profile.shadowMaterial, width);
            if (caption) caption.color = entry.textColor;
            if (graphic) { graphic.rectTransform.sizeDelta = entry.iconSize; graphic.rectTransform.anchoredPosition = entry.iconOffset; }
            if (shadow) { shadow.rectTransform.sizeDelta = entry.iconSize; shadow.rectTransform.anchoredPosition = entry.iconOffset + entry.iconShadowOffset; }
        }
        public void Fit() { ApplyPresentation(); scale = Mathf.Min(1, ((RectTransform)transform).rect.height / 64); Refresh(); }
        void Refresh() { if (artwork) artwork.localScale = Vector3.one * scale * (pressed ? .96f : hover ? 1.025f : 1); }
        void LateUpdate()
        {
            if(!graphic)return;
            var game=NormalGameStateManager.Instance;
            bool pending=WindowsManager.Instance && WindowsManager.Instance.CanReturnToGame
                && game && game.IsGameActive && game.IsSelfActionRequired;
            float progress=Mathf.PingPong(Time.unscaledTime/Mathf.Max(.05f,turnFlashCycleSeconds*.5f),1f);
            graphic.color=pending?Color.Lerp(turnFlashColorA,turnFlashColorB,progress):idleColor;
        }
        public void OnPointerEnter(PointerEventData e) { hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { hover = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }
    }
}
