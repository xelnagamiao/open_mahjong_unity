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
public abstract partial class TableSurfacePanel : MonoBehaviour
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
        public TableSurfaceColorLibrary.Entry Color;
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
    private bool scrollToNewColor;

    protected abstract bool IsCloth { get; }
    protected abstract GameObject ItemPrefab { get; }
    protected abstract Transform Content { get; }
    protected abstract Button DeleteButton { get; }
    public bool IsLoading => loading != null;
    public bool IsClothSurface => IsCloth;
    public (string path, bool isCustom) CurrentSelection => ConfigManager.Instance == null ? ("",false)
        : IsCloth ? ConfigManager.Instance.GetSelectedTableCloth() : ConfigManager.Instance.GetSelectedTableEdge();
    public void BeginNewColor() => TableSurfaceColorEditor.Ensure(this).Begin();
    public void SelectColor(string id)
    {
        if (ConfigManager.Instance == null) return;
        if (IsCloth) ConfigManager.Instance.SetSelectedTableCloth(id,true);
        else ConfigManager.Instance.SetSelectedTableEdge(id,true);
    }
    public void ReloadColors(bool revealNewColor = false)
    {
        scrollToNewColor |= revealNewColor;
        LoadGallery();
        GetComponent<TableSurfaceColorEditor>()?.RefreshSelection();
        GetComponent<TableFrameHeader>()?.RefreshSelection();
    }
    public void DeleteColor(string id)
    {
        TableSurfaceColorLibrary.Delete(id,()=>{
            if (this == null) return;
            if (CurrentSelection.path == id && ConfigManager.Instance != null) {
                if (IsCloth) ConfigManager.Instance.SetSelectedTableCloth("",false);
                else ConfigManager.Instance.SetSelectedTableEdge(TableFrameStyles.Default,false);
                Desktop.Instance?.RefreshAppearance();
            }
            ReloadColors();
        }, SceneConfigUi.ShowTip);
    }

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
        if (requested) LoadGallery();
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
            var manifest = Resources.Load<TextAsset>("image/Board/Previews/catalog");
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

        // Stable partition on both pages, including when an older preview catalog is loaded.
        Predicate<Source> isSolid = s => !s.Custom && (IsCloth
            ? TableClothStyles.IsSolid(s.Path) : TableFrameStyles.IsSolid(s.Path));
        var solids = sources.FindAll(isSolid);
        sources.RemoveAll(isSolid);
        sources.AddRange(solids);
        if (!TableSurfaceColorLibrary.IsReady) {
            bool returned=false;
            TableSurfaceColorLibrary.EnsureReady(()=>{if(returned && this!=null && requested && isActiveAndEnabled)LoadGallery();});
            returned=true;
        }
        foreach (var entry in TableSurfaceColorLibrary.GetEntries(IsCloth))
            sources.Add(new Source { Path=entry.id, Custom=true, Revision=entry, Color=entry });

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
                if (source.Color != null)
                    texture = CreateColorPreview(source.Color.DisplayColor, IsCloth);
                else if (IsCloth && !source.Custom && TableClothStyles.IsSolid(source.Path))
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
        if (scrollToNewColor)
        {
            // Let the grid/content fitter include the new last row before scrolling.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var scroll = Content != null ? Content.GetComponentInParent<ScrollRect>(true) : null;
            if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 0; }
            scrollToNewColor = false;
        }
        // IndexedDB may finish opening after the header's initial refresh.
        GetComponent<TableSurfaceColorEditor>()?.RefreshSelection();
        GetComponent<TableFrameHeader>()?.RefreshSelection();
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
        if (source.Color != null)
        {
            var labelObject = new GameObject("ColorName",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));
            labelObject.layer=row.Item.layer;labelObject.transform.SetParent(image.transform,false);
            var label=labelObject.GetComponent<TMPro.TextMeshProUGUI>();
            label.font=GetComponentInChildren<TMPro.TMP_Text>(true)?.font;label.text=source.Color.name;
            label.fontSize=16;label.color=Color.white;label.raycastTarget=false;
            label.alignment=TMPro.TextAlignmentOptions.Center;
            label.enableAutoSizing=true;label.fontSizeMin=9;label.fontSizeMax=16;
            label.textWrappingMode=TMPro.TextWrappingModes.NoWrap;label.overflowMode=TMPro.TextOverflowModes.Ellipsis;
            label.richText=false;
            // The image is inset by 8px in the gallery prefab; its baked strip is 25/128 of its height.
            var rect=label.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=new Vector2(1,25f/128f);
            rect.offsetMin=new Vector2(4,1);rect.offsetMax=new Vector2(-4,-1);
        }
        return row;
    }

    private static Texture2D CreateColorPreview(Color color, bool cloth)
    {
        // Small derived swatches only; the actual table continues to use shader parameters.
        const int size=128;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
            int edge=Mathf.Min(Mathf.Min(x,y),Mathf.Min(size-1-x,size-1-y));
            pixels[y*size+x]=cloth || edge<16 ? color : Color.clear;
            if(y<25)pixels[y*size+x]=new Color32(38,44,56,255);
        }
        texture.SetPixels32(pixels);texture.Apply(false,true);texture.wrapMode=TextureWrapMode.Clamp;
        return texture;
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
