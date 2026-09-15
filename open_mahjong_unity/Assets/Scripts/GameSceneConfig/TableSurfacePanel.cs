using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// The gallery owns only small previews. Full table textures are loaded by Desktop
/// when selected; never enumerate the full-resolution Resources folders here.
/// </summary>
public abstract class TableSurfacePanel : MonoBehaviour
{
    [Serializable] private class PreviewEntry { public string name; public string preview; public string displayName; }
    [Serializable] private class Catalog { public PreviewEntry[] cloth; public PreviewEntry[] edge; }
    private sealed class Source
    {
        public string Path;
        public string Preview;
        public object Revision;
        public bool Custom;
        public byte[] Bytes;
        public string Key => (Custom ? "custom:" : "builtin:") + Path;
    }
    private sealed class Row
    {
        public GameObject Item;
        public Sprite Sprite;
        public Texture2D OwnedTexture;
        public object Revision;
    }

    private static Catalog catalog;
    private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.Ordinal);
    private Coroutine loading;
    private UnityWebRequest customRequest;
    private bool requested;

    protected abstract bool IsCloth { get; }
    protected abstract GameObject ItemPrefab { get; }
    protected abstract Transform Content { get; }
    protected abstract Button DeleteButton { get; }
    public bool IsLoading => loading != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCatalog() => catalog = null;

    protected void LoadGallery()
    {
        requested = true;
        StopLoading();
        if (DeleteButton != null)
        {
            DeleteButton.gameObject.SetActive(false);
            DeleteButton.onClick.RemoveAllListeners();
        }
        if (isActiveAndEnabled) loading = StartCoroutine(LoadGuarded());
    }

    protected virtual void OnEnable()
    {
        ConfigureGalleryScrolling();
        if (requested) LoadGallery();
    }

    // Shared by cloth and frame galleries; no effect on dropdown popup scrollbars.
    public void ConfigureGalleryScrolling()
    {
        var scroll = Content != null ? Content.GetComponentInParent<ScrollRect>(true) : null;
        if (scroll == null) return;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 64f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = .135f;
        // Own the viewport inset explicitly instead of retaining the legacy -17px rail cutout.
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbarSpacing = 8;
        var bar = scroll.verticalScrollbar;
        if (scroll.viewport != null)
            StretchScrollRect(scroll.viewport, Vector2.zero, new Vector2(bar != null ? -24 : 0, 0));
        if (bar == null) return;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.numberOfSteps = 0;
        var rail = (RectTransform)bar.transform;
        rail.localScale = Vector3.one;
        rail.anchorMin = new Vector2(1, 0); rail.anchorMax = Vector2.one;
        rail.pivot = new Vector2(1, .5f);
        // Small inset keeps the square rail clear of the header/footer and panel edge.
        rail.offsetMin = new Vector2(-20, 6); rail.offsetMax = new Vector2(-4, -6);
        SquareScrollImage(bar.GetComponent<Image>(), new Color32(38, 46, 61, 255));
        if (bar.handleRect == null) return;
        var area = bar.handleRect.parent as RectTransform;
        if (area != null && area != rail)
            StretchScrollRect(area, new Vector2(3, 0), new Vector2(-3, 0));
        bar.handleRect.localScale = Vector3.one;
        bar.handleRect.offsetMin = bar.handleRect.offsetMax = Vector2.zero;
        var handle = bar.handleRect.GetComponent<Image>();
        SquareScrollImage(handle, Color.white);
        if (handle != null) bar.targetGraphic = handle;
        bar.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color32(136, 155, 187, 255);
        colors.highlightedColor = new Color32(182, 200, 227, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color32(88, 107, 204, 255);
        colors.disabledColor = new Color32(79, 89, 107, 255);
        colors.fadeDuration = .1f;
        bar.colors = colors;
    }

    private static void StretchScrollRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = min; rect.offsetMax = max;
    }

    private static void SquareScrollImage(Image image, Color color)
    {
        if (image == null) return;
        image.sprite = null; image.type = Image.Type.Simple;
        image.color = color;
    }

    protected virtual void OnDisable() => StopLoading();
    protected virtual void OnDestroy() => ClearGallery();

    private void StopLoading()
    {
        if (loading != null) StopCoroutine(loading);
        loading = null;
        if (customRequest != null)
        {
            customRequest.Abort();
            customRequest.Dispose();
            customRequest = null;
        }
    }

    private IEnumerator LoadGuarded()
    {
        var routine = Reconcile();
        try
        {
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception error)
                {
                    Debug.LogException(error, this);
                    break;
                }
                if (!more) break;
                yield return routine.Current;
            }
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
            if (customRequest != null)
            {
                customRequest.Abort();
                customRequest.Dispose();
                customRequest = null;
            }
            loading = null;
        }
    }

    private IEnumerator Reconcile()
    {
        // Give the newly selected tab a frame to appear before any IO or layout work.
        yield return null;
        if (catalog == null)
        {
            var manifest = Resources.Load<TextAsset>("TableSurfacePreviews/catalog");
            if (manifest != null)
            {
                catalog = JsonUtility.FromJson<Catalog>(manifest.text);
                Resources.UnloadAsset(manifest);
            }
            else Debug.LogError("桌面预览目录缺失，请重建 Table Surface Previews。");
        }

        var sources = new List<Source>();
        var builtins = IsCloth ? catalog?.cloth : catalog?.edge;
        if (builtins != null)
            foreach (var entry in builtins)
                sources.Add(new Source { Path = entry.name, Preview = entry.preview, Revision = entry.preview });

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!UnityAssetIdb.IsReady)
            UnityAssetIdb.EnsureReady(() =>
            {
                // Built-in previews are usable while IndexedDB is still opening.
                if (this != null && requested && isActiveAndEnabled) LoadGallery();
            });
        foreach (string key in UnityAssetIdb.KeysWithPrefix(IsCloth ? UnityAssetIdb.PrefixTablecloth : UnityAssetIdb.PrefixTableEdge))
        {
            byte[] bytes = UnityAssetIdb.GetCached(key);
            sources.Add(new Source { Path = key, Revision = bytes, Bytes = bytes, Custom = true });
        }
#else
        AddFileSources(sources);
#endif

        if (IsCloth)
        {
            var solids = sources.FindAll(s => !s.Custom && TableClothStyles.IsSolid(s.Path));
            sources.RemoveAll(s => !s.Custom && TableClothStyles.IsSolid(s.Path));
            sources.AddRange(solids);
        }

        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources) keep.Add(source.Key);
        foreach (string key in new List<string>(rows.Keys))
            if (!keep.Contains(key)) RemoveRow(key);

        int sibling = 0;
        foreach (var source in sources)
        {
            if (rows.TryGetValue(source.Key, out var row) && !Equals(row.Revision, source.Revision))
            {
                RemoveRow(source.Key);
                row = null;
            }
            if (row == null)
            {
                Texture2D texture = null;
                if (IsCloth && !source.Custom && TableClothStyles.IsSolid(source.Path))
                    texture = Texture2D.whiteTexture;
                else if (!source.Custom)
                {
                    var request = Resources.LoadAsync<Texture2D>(source.Preview);
                    yield return request;
                    texture = request.asset as Texture2D;
                }
                else
                {
                    Texture2D original = null;
#if UNITY_WEBGL && !UNITY_EDITOR
                    original = UnityAssetIdb.ToTexture(source.Bytes);
#else
                    customRequest = UnityWebRequestTexture.GetTexture(new Uri(source.Path).AbsoluteUri, true);
                    yield return customRequest.SendWebRequest();
                    if (customRequest.result == UnityWebRequest.Result.Success)
                        original = DownloadHandlerTexture.GetContent(customRequest);
                    else Debug.LogWarning("无法读取桌面预览: " + source.Path + ": " + customRequest.error);
                    customRequest.Dispose();
                    customRequest = null;
#endif
                    if (original != null)
                    {
                        try { texture = CreateSmallPreview(original); }
                        catch (Exception error) { Debug.LogWarning("无法生成桌面预览: " + source.Path + ": " + error.Message); }
                        finally { Destroy(original); }
                    }
                }
                if (texture == null) continue;
                row = CreateRow(source, texture);
                rows.Add(source.Key, row);
                row.Item.transform.SetSiblingIndex(sibling++);
                RefreshSelection(row);
                // Bound UI creation and custom image decoding to one new item per frame.
                yield return null;
            }
            else
            {
                row.Item.transform.SetSiblingIndex(sibling++);
                RefreshSelection(row);
            }
        }
    }

    private void AddFileSources(List<Source> sources)
    {
        string directory = Path.Combine(Application.persistentDataPath, IsCloth ? "Tablecloths" : "TableEdges");
        if (!Directory.Exists(directory)) return;
        try
        {
            string[] files = Directory.GetFiles(directory);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp" && extension != ".tga") continue;
                var info = new FileInfo(file);
                sources.Add(new Source { Path = file, Custom = true, Revision = info.Length + ":" + info.LastWriteTimeUtc.Ticks });
            }
        }
        catch (Exception error) { Debug.LogWarning("无法列出自定义桌面: " + error.Message); }
    }

    private Row CreateRow(Source source, Texture2D texture)
    {
        var row = new Row
        {
            Item = Instantiate(ItemPrefab, Content),
            Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect),
            OwnedTexture = source.Custom ? texture : null,
            Revision = source.Revision
        };
        Image image;
        if (IsCloth)
        {
            var item = row.Item.GetComponent<TableCloth>();
            item.filePath = source.Path;
            item.isCustom = source.Custom;
            image = item.tableClothImage;
        }
        else
        {
            var item = row.Item.GetComponent<TableEdge>();
            item.filePath = source.Path;
            item.isCustom = source.Custom;
            image = item.tableEdgeImage;
        }
        image.sprite = row.Sprite;
        image.color = Color.white;
        return row;
    }

    private void RefreshSelection(Row row)
    {
        if (IsCloth) row.Item.GetComponent<TableCloth>().RefreshSelection();
        else row.Item.GetComponent<TableEdge>().RefreshSelection();
    }

    private static Texture2D CreateSmallPreview(Texture2D original)
    {
        float scale = Mathf.Min(1f, 256f / Mathf.Max(original.width, original.height));
        int width = Mathf.Max(1, Mathf.RoundToInt(original.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(original.height * scale));
        var preview = SceneConfigTextureCapture.Copy(original, width, height, readable: false);
        preview.name = "TableSurfaceCustomPreview";
        preview.filterMode = FilterMode.Bilinear;
        preview.wrapMode = TextureWrapMode.Clamp;
        return preview;
    }

    protected void ClearSelection()
    {
        foreach (var row in rows.Values)
        {
            if (IsCloth) row.Item.GetComponent<TableCloth>().tableClothChoseImage.gameObject.SetActive(false);
            else row.Item.GetComponent<TableEdge>().tableEdgeChoseImage.gameObject.SetActive(false);
        }
    }

    protected void ClearGallery()
    {
        requested = false;
        StopLoading();
        foreach (string key in new List<string>(rows.Keys)) RemoveRow(key);
    }

    private void RemoveRow(string key)
    {
        Row row = rows[key];
        rows.Remove(key);
        if (row.Item != null) { row.Item.SetActive(false); Destroy(row.Item); }
        if (row.Sprite != null) Destroy(row.Sprite);
        if (row.OwnedTexture != null) Destroy(row.OwnedTexture);
    }
}
