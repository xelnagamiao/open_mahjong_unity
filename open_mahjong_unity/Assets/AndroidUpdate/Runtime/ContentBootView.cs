using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OmUpdate {
    // This screen belongs to the APK and can recover even if no game DLL loads.
    public sealed class ContentBootView : MonoBehaviour {
        Canvas canvas;
        Text message;
        GameObject actions;
        GameObject ownedEvents;
        public void Bind(ContentBoot owner) {
            var root = new GameObject("Android update", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image)); background.transform.SetParent(root.transform, false);
            var r = (RectTransform)background.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(.04f, .08f, .10f, 1);
            Font font = Font.CreateDynamicFontFromOSFont(new[] { "sans-serif", "Noto Sans CJK SC", "Microsoft YaHei" }, 24);
            message = AddText(background.transform, font, Localize("正在准备游戏…"), new Vector2(0, 90), new Vector2(1080, 220));
            actions = new GameObject("Actions", typeof(RectTransform)); actions.transform.SetParent(background.transform, false);
            var layout = actions.AddComponent<HorizontalLayoutGroup>(); layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false; layout.spacing = 12; layout.childAlignment = TextAnchor.MiddleCenter;
            r = (RectTransform)actions.transform; r.sizeDelta = new Vector2(1200, 150); r.anchoredPosition = new Vector2(0, -130);
            AddButton("去官网下载", owner.Website, font); AddButton("关闭游戏", owner.Exit, font);
            if (!FindFirstObjectByType<EventSystem>()) {
                var events = new GameObject("UpdateEvents", typeof(EventSystem)); events.transform.SetParent(transform, false);
                ownedEvents = events;
                // Input module is selected in the generated Android bootstrap scene
                // according to the project's existing active input setting.
                var inputType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputType != null) events.AddComponent(inputType);
                else events.AddComponent<StandaloneInputModule>();
            }
        }
        static Text AddText(Transform parent, Font font, string text, Vector2 position, Vector2 size) {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            var result = obj.GetComponent<Text>(); result.font = font; result.fontStyle = FontStyle.Normal; result.fontSize = 25;
            result.color = Color.white; result.alignment = TextAnchor.MiddleCenter; result.text = text; return result;
        }
        void AddButton(string title, UnityEngine.Events.UnityAction click, Font font) {
            var obj = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(actions.transform, false);
            ((RectTransform)obj.transform).sizeDelta = new Vector2(320, 64);
            obj.GetComponent<Image>().color = new Color(.14f, .30f, .33f);
            obj.GetComponent<Button>().onClick.AddListener(click);
            var label = AddText(obj.transform, font, Localize(title), Vector2.zero, new Vector2(304, 64));
            label.raycastTarget = false;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 16; label.resizeTextMaxSize = 25;
        }
        public void Set(string text, bool failure, bool codeLoaded) {
            canvas.enabled = true; message.text = Localize(text); actions.SetActive(failure);
        }
        // Recovery must also work before the hot game assembly/catalog can load.
        static string Localize(string text) {
            int fallback = Application.systemLanguage switch {
                SystemLanguage.Chinese or SystemLanguage.ChineseSimplified => 0,
                SystemLanguage.ChineseTraditional => 1,
                SystemLanguage.French => 3,
                SystemLanguage.German => 4,
                SystemLanguage.Russian => 5,
                SystemLanguage.Japanese => 6,
                SystemLanguage.Spanish => 7,
                _ => 2
            };
            return ContentBootLocalization.Text(text, PlayerPrefs.GetInt("AppLanguage", fallback));
        }

        public void Hide() {
            canvas.enabled = false;
            if (ownedEvents) Destroy(ownedEvents);
        }
    }
}
