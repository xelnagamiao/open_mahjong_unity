using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 活动详情：常亮。顶部名称，下方按块顺序展示文本、小图、大图。
/// </summary>
public class ActivityDetailPanel : MonoBehaviour {
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform bodyContent;
    [SerializeField] private GameObject imageTemplate;
    [SerializeField] private ScrollRect bodyScroll;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private Coroutine _loadRoutine;

    private void Awake() {
        gameObject.SetActive(true);
        if (closeButton != null) closeButton.gameObject.SetActive(false);
        if (bodyText != null) bodyText.gameObject.SetActive(false);
        if (imageTemplate != null) imageTemplate.SetActive(false);
        VerticalLayoutGroup vlg = bodyContent != null
            ? bodyContent.GetComponent<VerticalLayoutGroup>()
            : null;
        if (vlg != null) {
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
        }
    }

    public void ShowEmpty() {
        gameObject.SetActive(true);
        if (titleText != null) titleText.text = "";
        if (statusText != null) statusText.gameObject.SetActive(false);
        if (bodyText != null) {
            bodyText.text = "";
            bodyText.gameObject.SetActive(false);
        }
        ClearSpawned();
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
        ClearSpawned();
        if (bodyScroll != null) bodyScroll.verticalNormalizedPosition = 1f;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadBlocks(ActivityHttp.BlocksOf(detail)));
    }

    private IEnumerator LoadBlocks(ActivityContentBlock[] blocks) {
        if (bodyText != null) bodyText.gameObject.SetActive(false);
        if (imageTemplate != null) imageTemplate.SetActive(false);
        if (blocks == null || bodyContent == null) yield break;
        for (int i = 0; i < blocks.Length; i++) {
            ActivityContentBlock block = blocks[i];
            if (block == null) continue;
            if (block.type == "text") {
                if (!string.IsNullOrEmpty(block.text)) SpawnTextBlock(block);
                continue;
            }
            if (block.type != "image" || string.IsNullOrEmpty(block.url)) continue;
            Texture2D texture = null;
            string error = null;
            yield return ActivityHttp.GetTexture(block.url, tex => texture = tex, err => error = err);
            if (texture == null) {
                Debug.LogWarning($"活动图片加载失败: {block.url} {error}");
                continue;
            }
            _textures.Add(texture);
            _spawned.Add(SpawnImageBlock(block, texture));
        }
        if (bodyScroll != null) bodyScroll.verticalNormalizedPosition = 1f;
    }

    private void SpawnTextBlock(ActivityContentBlock block) {
        if (bodyText == null) return;
        GameObject go = Instantiate(bodyText.gameObject, bodyContent);
        go.name = "ActivityText";
        go.SetActive(true);
        TMP_Text tmp = go.GetComponent<TMP_Text>();
        if (tmp != null) {
            tmp.richText = true;
            tmp.raycastTarget = ActivityHttp.HasTmpLink(block.text);
            tmp.fontSize = block.fontSize > 0 ? block.fontSize : 22;
            tmp.text = ActivityHttp.ToTmpRichText(block.text);
            if (tmp.raycastTarget && go.GetComponent<ActivityTmpLinkOpener>() == null) {
                go.AddComponent<ActivityTmpLinkOpener>();
            }
        }
        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (layout != null) {
            layout.flexibleWidth = 1f;
            layout.minHeight = 40f;
        }
        StretchFullWidth(go);
        _spawned.Add(go);
    }

    private GameObject SpawnImageBlock(ActivityContentBlock block, Texture2D texture) {
        GameObject slot = new GameObject("ActivityImage", typeof(RectTransform));
        slot.transform.SetParent(bodyContent, false);
        StretchFullWidth(slot);
        LayoutElement slotLayout = slot.AddComponent<LayoutElement>();
        slotLayout.flexibleWidth = 1f;
        GameObject go = imageTemplate != null
            ? Instantiate(imageTemplate, slot.transform)
            : new GameObject("Raw", typeof(RectTransform), typeof(RawImage), typeof(CanvasRenderer));
        go.SetActive(true);
        LayoutElement inner = go.GetComponent<LayoutElement>();
        if (inner != null) inner.ignoreLayout = true;
        RawImage raw = go.GetComponent<RawImage>() ?? go.GetComponentInChildren<RawImage>(true);
        if (raw == null) raw = go.AddComponent<RawImage>();
        raw.texture = texture;
        raw.color = Color.white;
        float contentW = ContentWidth();
        bool large = block.size != "small";
        RectTransform rt = go.transform as RectTransform;
        if (large) {
            float h = contentW * texture.height / Mathf.Max(1, texture.width);
            slotLayout.preferredHeight = h;
            slotLayout.minHeight = h;
            if (rt != null) {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        } else {
            float maxW = Mathf.Min(324f, contentW);
            float w = Mathf.Min(texture.width, maxW);
            float h = w * texture.height / Mathf.Max(1, texture.width);
            slotLayout.preferredHeight = h;
            slotLayout.minHeight = h;
            if (rt != null) {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = Vector2.zero;
            }
        }
        WireImageLink(go, raw, block.href);
        return slot;
    }

    private static void StretchFullWidth(GameObject go) {
        RectTransform rt = go.transform as RectTransform;
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
        rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
    }

    private float ContentWidth() {
        RectTransform rt = bodyContent as RectTransform;
        if (rt != null && rt.rect.width > 8f) return Mathf.Max(8f, rt.rect.width - 16f);
        return 880f;
    }

    private static void WireImageLink(GameObject go, RawImage raw, string href) {
        if (string.IsNullOrEmpty(href)) {
            if (raw != null) raw.raycastTarget = false;
            return;
        }
        if (raw != null) raw.raycastTarget = true;
        Button button = go.GetComponent<Button>();
        if (button == null) button = go.AddComponent<Button>();
        if (button.targetGraphic == null && raw != null) button.targetGraphic = raw;
        button.transition = Selectable.Transition.None;
        string captured = href;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => ActivityHttp.OpenHref(captured));
    }

    private void ClearSpawned() {
        for (int i = 0; i < _spawned.Count; i++) {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
        ActivityHttp.DestroyTextures(_textures);
    }

    private void OnDisable() {
        if (_loadRoutine != null) {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }
        ClearSpawned();
    }
}
