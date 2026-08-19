using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 活动详情：顶部固定名称，下方滚动展示正文和图片。
/// </summary>
public class ActivityDetailPanel : MonoBehaviour {
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform bodyContent;
    [SerializeField] private GameObject imageTemplate;
    [SerializeField] private ScrollRect bodyScroll;

    private readonly List<GameObject> _spawnedImages = new List<GameObject>();
    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private Coroutine _loadRoutine;

    public void Wire(System.Action onClose) {
        if (closeButton == null) closeButton = transform.Find("Header/Close")?.GetComponent<Button>();
        if (closeButton == null) return;
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(() => onClose?.Invoke());
    }

    public void Open(ActivityDetail detail) {
        gameObject.SetActive(true);
        if (titleText != null) {
            titleText.text = detail != null && !string.IsNullOrEmpty(detail.title)
                ? detail.title
                : "活动";
        }
        bool ended = detail != null && ActivityStatus.IsEnded(detail.status, detail.ended);
        if (statusText == null) {
            Transform found = transform.Find("Header/Status");
            if (found != null) statusText = found.GetComponent<TMP_Text>();
        }
        if (statusText != null) {
            statusText.text = "活动已结束";
            statusText.gameObject.SetActive(ended);
        }
        if (bodyText != null) {
            string body = detail != null ? detail.body : null;
            if (string.IsNullOrEmpty(body)) body = "暂无正文";
            bodyText.text = ended && statusText == null ? "活动已结束\n\n" + body : body;
        }
        ClearImages();
        if (bodyScroll != null) bodyScroll.verticalNormalizedPosition = 1f;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadImages(detail != null ? detail.image_urls : null));
    }

    public void Close() {
        if (_loadRoutine != null) {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }
        ClearImages();
        gameObject.SetActive(false);
    }

    private IEnumerator LoadImages(string[] urls) {
        if (urls == null || bodyContent == null || imageTemplate == null) yield break;
        for (int i = 0; i < urls.Length; i++) {
            string url = urls[i];
            if (string.IsNullOrEmpty(url)) continue;
            Texture2D texture = null;
            string error = null;
            yield return ActivityHttp.GetTexture(url, tex => texture = tex, err => error = err);
            if (texture == null) {
                Debug.LogWarning($"活动图片加载失败: {url} {error}");
                continue;
            }
            _textures.Add(texture);
            GameObject go = Instantiate(imageTemplate, bodyContent);
            go.SetActive(true);
            RawImage raw = go.GetComponent<RawImage>() ?? go.GetComponentInChildren<RawImage>(true);
            if (raw != null) {
                raw.texture = texture;
                raw.color = Color.white;
            }
            FitImageHeight(go, texture);
            _spawnedImages.Add(go);
        }
    }

    private static void FitImageHeight(GameObject go, Texture texture) {
        if (go == null || texture == null) return;
        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (layout == null) layout = go.AddComponent<LayoutElement>();
        RectTransform rt = go.transform as RectTransform;
        float width = rt != null && rt.rect.width > 8f ? rt.rect.width : 520f;
        layout.preferredHeight = width * texture.height / Mathf.Max(1, texture.width);
        layout.minHeight = layout.preferredHeight;
    }

    private void ClearImages() {
        for (int i = 0; i < _spawnedImages.Count; i++) {
            if (_spawnedImages[i] != null) Destroy(_spawnedImages[i]);
        }
        _spawnedImages.Clear();
        ActivityHttp.DestroyTextures(_textures);
    }

    private void OnDisable() {
        if (_loadRoutine != null) {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }
        ClearImages();
    }
}
