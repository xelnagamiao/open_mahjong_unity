using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class CardPackBrowser : MonoBehaviour
{
    public sealed class Item { public int submission_id; public string name, username, description, original_filename; public long file_size; }
    sealed class Catalog { public bool success; public Data data; }
    sealed class Data { public Item[] items; }
    TMP_FontAsset font;
    TMP_Text status;
    RectTransform content, scrollRoot;
    Action<byte[], string> picked;
    UnityWebRequest request, previewRequest;
    readonly List<Button> buttons = new List<Button>();
    readonly List<Texture2D> previewTextures = new List<Texture2D>();
    static CardPackBrowser current;
    static string Endpoint => ConfigManager.webApiUrl.TrimEnd('/') + "/api/player/tile-content";

    public static void Open(Transform owner, TMP_FontAsset font, Action<byte[], string> picked)
    {
        if (current != null) { current.transform.SetAsLastSibling(); return; }
        var canvas = owner.GetComponentInParent<Canvas>().rootCanvas;
        var root = new GameObject("CardPackBrowser", typeof(RectTransform), typeof(Image), typeof(CardPackBrowser));
        root.transform.SetParent(canvas.transform, false); root.layer = canvas.gameObject.layer;
        var view = root.GetComponent<CardPackBrowser>(); current = view; view.font = font; view.picked = picked;
        Stretch((RectTransform)root.transform); root.GetComponent<Image>().color = new Color(0, 0, 0, .65f);
        var panel = view.Rect("Panel", root.transform); panel.anchorMin = new Vector2(.14f, .12f); panel.anchorMax = new Vector2(.86f, .88f);
        panel.offsetMin = panel.offsetMax = Vector2.zero; panel.gameObject.AddComponent<Image>().color = new Color32(32, 40, 54, 255);
        view.Label(panel, "下载牌包", new Vector2(28, -22), new Vector2(600, 52), 32);
        view.status = view.Label(panel, "", new Vector2(28, -78), new Vector2(950, 42), 22);
        var close = view.ActionButton(panel, "关闭", () => Destroy(root));
        AnchorRight((RectTransform)close.transform, -24, -24, 120, 48);
        var upload = view.ActionButton(panel, "去上传", () => Application.OpenURL(ConfigManager.webUrl.TrimEnd('/') + "/account#sec-uploads"));
        AnchorRight((RectTransform)upload.transform, -156, -24, 120, 48);
        var scrollRoot = view.scrollRoot = view.Rect("Scroll", panel); Stretch(scrollRoot); scrollRoot.offsetMin = new Vector2(24, 24);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
        var viewport = view.Rect("Viewport", scrollRoot); Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
        view.content = view.Rect("Content", viewport); view.content.anchorMin = new Vector2(0, 1); view.content.anchorMax = Vector2.one;
        view.content.pivot = new Vector2(.5f, 1); view.content.sizeDelta = Vector2.zero;
        var layout = view.content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandHeight = false;
        view.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = view.content; scroll.movementType = ScrollRect.MovementType.Clamped;
        view.StartCoroutine(view.LoadCatalog());
    }
    void SetStatus(string message)
    {
        status.text = message;
        status.gameObject.SetActive(message.Length > 0);
        scrollRoot.offsetMax = new Vector2(-24, message.Length > 0 ? -128 : -88);
    }
    IEnumerator LoadCatalog()
    {
        SetStatus("正在读取牌包…");
        request = UnityWebRequest.Get(Endpoint + "/catalog?kind=tile_face"); request.timeout = 30;
        yield return request.SendWebRequest();
        Item[] items = null;
        if (request.result == UnityWebRequest.Result.Success && request.downloadedBytes <= 1024 * 1024) {
            try { var catalog = JsonConvert.DeserializeObject<Catalog>(request.downloadHandler.text); if (catalog?.success == true) items = catalog.data?.items; }
            catch (JsonException) { }
        }
        request.Dispose(); request = null;
        if (items == null) { SetStatus("读取失败，请重新打开牌包列表"); yield break; }
        SetStatus(items.Length == 0 ? "暂无可下载的牌包" : "");
        var previews = new List<(int id, RawImage image)>();
        foreach (var item in items) {
            if (item == null || item.submission_id <= 0) continue;
            var row = Rect("Pack_" + item.submission_id, content);
            row.gameObject.AddComponent<Image>().color = new Color32(47, 59, 78, 255);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 146;
            var preview = Rect("FacePreview", row);
            preview.anchorMin = preview.anchorMax = new Vector2(0, .5f); preview.pivot = new Vector2(0, .5f);
            preview.anchoredPosition = new Vector2(16, 0); preview.sizeDelta = new Vector2(201, 68);
            var image = preview.gameObject.AddComponent<RawImage>(); image.raycastTarget = false; image.enabled = false;
            previews.Add((item.submission_id, image));
            var title = Label(row, item.name, new Vector2(236, -14), new Vector2(800, 44), 28);
            title.rectTransform.anchorMax = Vector2.one; title.rectTransform.offsetMax = new Vector2(-190, -14);
            var author = Label(row, item.username + " · " + (item.file_size / 1048576f).ToString("0.0") + " MB", new Vector2(236, -60), new Vector2(800, 30), 20);
            author.rectTransform.anchorMax = Vector2.one; author.rectTransform.offsetMax = new Vector2(-190, -60);
            author.color = new Color32(190, 202, 220, 255);
            var description = Label(row, item.description ?? "", new Vector2(236, -96), new Vector2(800, 30), 20);
            description.rectTransform.anchorMax = Vector2.one; description.rectTransform.offsetMax = new Vector2(-190, -96);
            var download = ActionButton(row, "下载", () => StartCoroutine(Download(item)));
            AnchorRight((RectTransform)download.transform, -20, -48, 140, 52); buttons.Add(download);
        }
        foreach (var preview in previews) {
            previewRequest = UnityWebRequestTexture.GetTexture(Endpoint + "/" + preview.id + "/preview", true);
            previewRequest.timeout = 15;
            yield return previewRequest.SendWebRequest();
            if (previewRequest.result == UnityWebRequest.Result.Success) {
                var texture = DownloadHandlerTexture.GetContent(previewRequest);
                previewTextures.Add(texture);
                preview.image.texture = texture;
                preview.image.enabled = true;
            }
            previewRequest.Dispose(); previewRequest = null;
        }
    }
    IEnumerator Download(Item item)
    {
        if (request != null) yield break;
        foreach (var button in buttons) button.interactable = false;
        request = UnityWebRequest.Get(Endpoint + "/" + item.submission_id + "/file"); request.timeout = 120;
        var operation = request.SendWebRequest();
        while (!operation.isDone) {
            SetStatus("正在下载 " + item.name + " · " + (request.downloadedBytes / 1048576f).ToString("0.0") + " MB");
            if (request.downloadedBytes > TilePackImporter.MaxUncompressedBytes) { request.Abort(); break; }
            yield return null;
        }
        bool success = request.result == UnityWebRequest.Result.Success && request.downloadedBytes <= TilePackImporter.MaxUncompressedBytes;
        byte[] bytes = success ? request.downloadHandler.data : null;
        request.Dispose(); request = null;
        if (!success) { SetStatus("下载失败，请重试"); foreach (var button in buttons) button.interactable = true; yield break; }
        var callback = picked; Destroy(gameObject); callback?.Invoke(bytes, item.original_filename ?? item.name + ".zip");
    }
    void OnDestroy() {
        if (request != null) { request.Abort(); request.Dispose(); }
        if (previewRequest != null) { previewRequest.Abort(); previewRequest.Dispose(); }
        foreach (var texture in previewTextures) Destroy(texture);
        if (current == this) current = null;
        picked = null;
    }
    void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Destroy(gameObject); }
    RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    TMP_Text Label(Transform parent, string caption, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect("Label", parent); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = caption; text.fontSize = fontSize;
        text.color = Color.white; text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular; text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        return text;
    }
    Button ActionButton(Transform parent, string caption, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(caption, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = SceneConfigUi.CardHeaderAction;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; SceneConfigUi.ConfigureButtonFeedback(button, image.color); button.onClick.AddListener(action);
        var text = Label(rect, caption, Vector2.zero, Vector2.zero, 24); Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.Center;
        return button;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    static void AnchorRight(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
}
