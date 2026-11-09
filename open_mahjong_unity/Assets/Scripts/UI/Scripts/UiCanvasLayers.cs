using UnityEngine;

/// <summary>Shared drawing layers for authored UI and runtime-created popups.</summary>
public static class UiCanvasLayers {
    public const string Board = "BoardUI";
    public const string Page = "PageUI";
    public const string Navigation = "NavigationUI";
    public const string Overlay = "OverlayUI";
    public const string Modal = "ModalUI";
    public const string Design = "DesignUI";
    public const string Feedback = "FeedbackUI";

    // Overlay rendering and camera rendering must agree on both the layer and the order.
    public const int SettlementOrder = 20;
    public const int NavigationOrder = 1000;
    public const int OverlayOrder = 2000;
    public const int ChatOrder = OverlayOrder + 200;
    public const int RulebookOrder = OverlayOrder + 220;
    public const int InventoryOrder = OverlayOrder + 260;
    public const int MailOrder = OverlayOrder + 290;
    public const int RankingOrder = OverlayOrder + 500;
    public const int PlayerInfoOrder = OverlayOrder + 600;
    public const int DetailsOrder = OverlayOrder + 800;
    public const int DesignOptionsOrder = 19000;
    public const int ModalOrder = 12000;
    public const int FeedbackOrder = 20000;

    public static Canvas SortingCanvas(Transform child) {
        for (Transform current = child; current; current = current.parent) {
            Canvas canvas = current.GetComponent<Canvas>();
            if (canvas && (canvas.overrideSorting || canvas.isRootCanvas)) return canvas;
        }
        return null;
    }

    public static void Apply(Canvas canvas, string layer, int order, bool independent = true) {
        int id = SortingLayer.NameToID(layer);
        // Active overlay canvases ignore a sorting-layer assignment until they are disabled.
        bool reenable = canvas.enabled && (canvas.sortingLayerID != id || canvas.overrideSorting != independent);
        if (reenable) canvas.enabled = false;
        canvas.sortingLayerID = id;
        if (reenable) canvas.enabled = true;
        // A newly added nested Canvas only accepts overrideSorting while enabled.
        canvas.overrideSorting = independent;
        canvas.sortingOrder = order;
    }
}
