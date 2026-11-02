#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.DataLab;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MatchDetailUiBaker
{

    public static void Bake(Scene scene)
    {
        foreach (var page in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HomeDataPanel>(true)))
            Bake(page);
    }

    static void Bake(HomeDataPanel page)
    {
        var detail = page.detail;
        if (!detail) return;
        var root = (RectTransform)detail.transform;
        root.sizeDelta = new Vector2(780, 392);
        RemoveLabel(page, root, "Title");
        RemoveLabel(page, root, "Room");

        var header = Container(root, "Header");
        Place(header, 0, 0, 780, 68);
        header.SetAsFirstSibling();
        var fill = header.GetComponent<Image>() ?? header.gameObject.AddComponent<Image>();
        fill.raycastTarget = false;
        Tint(page, fill, HomeColor.Accent);
        Label(page, detail.ownRank, header, 32, 12, 160, 44, 22, HomeColor.AccentInk);
        Label(page, detail.ownScore, header, 208, 12, 160, 44, 22, HomeColor.AccentInk);
        Label(page, detail.date, header, 392, 12, 300, 44, 18, HomeColor.AccentInk);
        Reparent(page.closeDetail.transform, header, 712, 14, 40, 40);

        for (int i = 0; i < detail.seats.Count; i++)
            Place((RectTransform)detail.seats[i].root.transform, 32, 84 + i * 54, 716, 48);
        var divider = root.Find("Divider") as RectTransform;
        if (divider) Place(divider, 32, 308, 716, 1);

        var footer = Container(root, "Footer");
        Place(footer, 32, 324, 716, 44);
        Label(page, detail.gameId, footer, 0, 0, 396, 44, 16, HomeColor.Ink);
        Reparent(detail.replay2D.transform, footer, 412, 0, 142, 44);
        Reparent(detail.replay3D.transform, footer, 570, 0, 146, 44);
        EditorUtility.SetDirty(detail);
        EditorUtility.SetDirty(page);
    }

    public static void Validate(Scene scene)
    {
        foreach (var page in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HomeDataPanel>(true)))
        {
            var detail = page.detail;
            if (!detail) continue;
            var root = (RectTransform)detail.transform;
            var header = root.Find("Header");
            var footer = root.Find("Footer");
            if (!header || !footer || root.Find("Title") || root.Find("Room"))
                throw new InvalidOperationException("Match detail needs the compact header and footer without the retired description.");
            if (!detail.ownRank || !detail.ownScore || !detail.date || !detail.gameId || !detail.replay2D || !detail.replay3D || !page.closeDetail)
                throw new InvalidOperationException("Match detail is missing a text or button binding.");
            if (new[] { detail.ownRank.transform, detail.ownScore.transform, detail.date.transform, page.closeDetail.transform }.Any(item => item.parent != header)
                || new[] { detail.gameId.transform, detail.replay2D.transform, detail.replay3D.transform }.Any(item => item.parent != footer))
                throw new InvalidOperationException("Match detail header metadata and replay footer are not grouped correctly.");
            var fill = header.GetComponent<Image>();
            if (!fill || fill.raycastTarget || !page.tints.Any(item => item.graphic == fill && item.color == HomeColor.Accent))
                throw new InvalidOperationException("Match detail header must use the existing palette accent without blocking input.");
        }
    }

    static RectTransform Container(Transform parent, string name)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect)
        {
            rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
        }
        return rect;
    }

    static void RemoveLabel(HomeDataPanel page, Transform parent, string name)
    {
        var child = parent.Find(name);
        if (!child) return;
        var graphics = child.GetComponentsInChildren<Graphic>(true);
        page.tints.RemoveAll(item => item == null || !item.graphic || graphics.Contains(item.graphic));
        Object.DestroyImmediate(child.gameObject);
    }

    static void Tint(HomeDataPanel page, Graphic graphic, HomeColor color)
    {
        page.tints.RemoveAll(item => item != null && item.graphic == graphic);
        page.tints.Add(new HomeTint { graphic = graphic, color = color });
        graphic.color = page.ActivePalette.colors[(int)color];
        EditorUtility.SetDirty(graphic);
    }

    static void Label(HomeDataPanel page, TMP_Text label, Transform parent,
        float x, float y, float width, float height, float size, HomeColor color)
    {
        Reparent(label.transform, parent, x, y, width, height);
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.fontSize = label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Min(14, size);
        label.enableAutoSizing = true;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
        var localized = label.GetComponent<LocalizedPlatformLabel>();
        if (localized)
        {
            localized.authoredSize = size;
            localized.minimumSize = label.fontSizeMin;
            localized.authoredAutoSize = true;
            EditorUtility.SetDirty(localized);
        }
        Tint(page, label, color);
    }

    static void Reparent(Transform target, Transform parent, float x, float y, float width, float height)
    {
        target.SetParent(parent, false);
        Place((RectTransform)target, x, y, width, height);
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        EditorUtility.SetDirty(rect);
    }
}
#endif
