#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public abstract partial class TableSurfacePanel
{
    // Baked once; gallery input no longer restyles authored scrollbars on every open.
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
}
#endif
