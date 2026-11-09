using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits the authored login layout without replacing its typography or control geometry.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(300)]
public sealed class MobileLoginViewport : MonoBehaviour {
    const float Margin = 32;
    RectTransform panel, safeSpace;
    CanvasReferenceLayout safeLayout;
    CanvasReferenceLayout languageLayout;
    RectTransform languageLayer;
    AuthoredRect header;
    AuthoredRect[] headerChildren, columns;
    Vector2 lastSize;
    Rect lastFullBounds;
    float left, right, bottom, top;
    float headerHeight;
    readonly Vector3[] corners = new Vector3[4];

    struct AuthoredRect {
        public RectTransform rect;
        public Vector2 position, size;
        public Vector3 scale;
        public AuthoredRect(RectTransform value) {
            rect = value; position = value.anchoredPosition;
            size = value.sizeDelta; scale = value.localScale;
        }
    }

    public static void Install(LoginPanel login, RectTransform safeArea, CanvasScaler settings) {
        if (!MobileViewportLayout.Enabled || !login || login.GetComponent<MobileLoginViewport>()) return;
        var view = login.gameObject.AddComponent<MobileLoginViewport>();
        view.Initialize(login, safeArea, settings);
    }

    void Initialize(LoginPanel login, RectTransform safeArea, CanvasScaler settings) {
        panel = (RectTransform)login.transform;
        var children = new List<Transform>();
        foreach (Transform child in panel) children.Add(child);
        // The existing black background fills the real display. The existing
        // controls keep the same safe-area coordinate space they used before.
        panel.SetParent(safeArea.parent, false);
        panel.SetSiblingIndex(safeArea.GetSiblingIndex());
        safeSpace = new GameObject("MobileLoginSafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
        safeSpace.gameObject.layer = gameObject.layer;
        safeSpace.SetParent(panel, false);
        foreach (var child in children) child.SetParent(safeSpace, false);
        safeLayout = safeSpace.gameObject.AddComponent<CanvasReferenceLayout>();
        safeLayout.referenceArea = safeArea; safeLayout.scaleSettings = settings;
        safeLayout.Apply(true);

        var body = new List<AuthoredRect>();
        foreach (string name in new[] { "TEXT", "TipsText", "LoginPanel" }) {
            var rect = safeSpace.Find(name) as RectTransform;
            if (!rect) continue;
            body.Add(new AuthoredRect(rect));
            left = Mathf.Min(left, rect.anchoredPosition.x + rect.rect.xMin * rect.localScale.x);
            right = Mathf.Max(right, rect.anchoredPosition.x + rect.rect.xMax * rect.localScale.x);
            bottom = Mathf.Min(bottom, rect.anchoredPosition.y + rect.rect.yMin * rect.localScale.y);
            top = Mathf.Max(top, rect.anchoredPosition.y + rect.rect.yMax * rect.localScale.y);
        }
        columns = body.ToArray();
        var band = safeSpace.Find("ConnectHeader") as RectTransform;
        var foreground = new List<AuthoredRect>();
        if (band) {
            header = new AuthoredRect(band);
            // Resolve the authored percentage/offset combination once at its
            // reference height, so changing only width cannot thin the bar.
            headerHeight = (band.anchorMax.y - band.anchorMin.y) * settings.referenceResolution.y + band.sizeDelta.y;
            foreach (Transform child in band)
                if (child is RectTransform rect) foreground.Add(new AuthoredRect(rect));
        }
        headerChildren = foreground.ToArray();
        languageLayer = safeSpace.Find("LanguageAccessLayer") as RectTransform;
        if (languageLayer) {
            languageLayout = languageLayer.GetComponent<CanvasReferenceLayout>() ?? languageLayer.gameObject.AddComponent<CanvasReferenceLayout>();
            languageLayout.referenceArea = safeArea; languageLayout.scaleSettings = settings;
            languageLayout.keepMobilePopupSize = true; languageLayout.Apply(true);
        }
        Apply(true);
    }

    void OnEnable() { if (safeLayout) Apply(true); }
    void LateUpdate() { if (safeLayout) Apply(false); }

    void Apply(bool force) {
        safeLayout.Apply();
        if (languageLayout) languageLayout.Apply();
        panel.GetWorldCorners(corners);
        Vector2 minimum = safeSpace.InverseTransformPoint(corners[0]);
        Vector2 maximum = safeSpace.InverseTransformPoint(corners[2]);
        var full = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        Vector2 size = safeSpace.rect.size;
        if (!force && size == lastSize && full == lastFullBounds) return;
        lastSize = size; lastFullBounds = full;
        float headerScale = Screen.height / Mathf.Max(1, safeLayout.scaleSettings.referenceResolution.y)
            / Mathf.Max(.001f, CanvasReferenceLayout.PixelsPerUnit(safeSpace));

        float horizontal = Mathf.Max(-left, right);
        float scale = horizontal > 0 ? Mathf.Min(1, Mathf.Max(1, size.x * .5f - Margin) / horizontal) : 1;
        if (bottom < 0) scale = Mathf.Min(scale, Mathf.Max(1, size.y * .5f - Margin) / -bottom);
        if (header.rect && top > 0) {
            float belowHeader = safeSpace.rect.yMax - headerHeight * headerScale;
            scale = Mathf.Min(scale, Mathf.Max(1, belowHeader - Margin) / top);
        }
        foreach (var authored in columns) {
            authored.rect.anchoredPosition = authored.position * scale;
            authored.rect.localScale = new Vector3(authored.scale.x * scale, authored.scale.y * scale, authored.scale.z);
        }

        if (!header.rect) return;
        float insetLeft = Mathf.Max(0, safeSpace.rect.xMin - full.xMin) / headerScale;
        float insetRight = Mathf.Max(0, full.xMax - safeSpace.rect.xMax) / headerScale;
        float insetTop = Mathf.Max(0, full.yMax - safeSpace.rect.yMax) / headerScale;
        header.rect.anchorMin = Vector2.up; header.rect.anchorMax = Vector2.one;
        header.rect.pivot = new Vector2(.5f, 1);
        header.rect.localScale = new Vector3(headerScale, headerScale, 1);
        header.rect.sizeDelta = new Vector2(full.width / headerScale - size.x, headerHeight + insetTop);
        header.rect.anchoredPosition = new Vector2(full.center.x - safeSpace.rect.center.x, full.yMax - safeSpace.rect.yMax);
        // Background, status text and language controls share a height-based
        // scale. Only the background extends beyond the safe-area edges.
        foreach (var authored in headerChildren) {
            var rect = authored.rect;
            Vector2 span = rect.anchorMax - rect.anchorMin;
            Vector2 anchor = rect.anchorMin + Vector2.Scale(span, rect.pivot);
            rect.sizeDelta = authored.size - Vector2.Scale(span, new Vector2(insetLeft + insetRight, insetTop));
            rect.anchoredPosition = authored.position + new Vector2(insetLeft, 0)
                - Vector2.Scale(anchor, new Vector2(insetLeft + insetRight, insetTop));
            if (languageLayer && rect.GetComponent<TMPro.TMP_Text>()) {
                float rightEdge = header.rect.rect.xMax;
                foreach (Transform control in languageLayer) {
                    if (control.name != "LanguageSelector" && control.name != "LanguageCaption") continue;
                    ((RectTransform)control).GetWorldCorners(corners);
                    rightEdge = Mathf.Min(rightEdge, header.rect.InverseTransformPoint(corners[0]).x - 16);
                }
                // Reserve the real language controls' hit area for long status messages.
                float anchorRight = header.rect.rect.xMin + header.rect.rect.width * rect.anchorMax.x;
                rect.offsetMax = new Vector2(Mathf.Min(rect.offsetMax.x, rightEdge - anchorRight), rect.offsetMax.y);
            }
        }
    }
}
