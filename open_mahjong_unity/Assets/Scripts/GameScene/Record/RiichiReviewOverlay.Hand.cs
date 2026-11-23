using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class RiichiReviewOverlay {
    private struct Face {
        public int Tile;
        public Rect Bounds;
        public bool Drawn;
    }
    private sealed class Marker {
        public RectTransform Root, Badge, Bar, Bracket;
        public TableHudSurface Surface;
        public Image BarImage;
        public TextMeshProUGUI Label;
    }
    private readonly List<Face> faces = new List<Face>(14);
    private readonly List<Marker> markers = new List<Marker>(14);
    private readonly Vector3[] worldCorners = new Vector3[4];
    private bool pressStartedOnHand, touchWasLastInput;
    private Vector2 lastTouchPosition;
    private Camera eventCamera;

    private void CollectFaces() {
        faces.Clear();
        var hand = GameCanvas.Instance?.HandCardsContainer;
        if (!hand || !hand.gameObject.activeInHierarchy) return;
        for (int i = 0; i < hand.childCount; i++) {
            var child = hand.GetChild(i);
            if (!child.gameObject.activeInHierarchy || !child.TryGetComponent<TileCard>(out var card)) continue;
            var body = card.BadgeBody;
            if (!body || !body.gameObject.activeInHierarchy) continue;
            body.rectTransform.GetWorldCorners(worldCorners);
            Vector2 min = root.InverseTransformPoint(worldCorners[0]), max = root.InverseTransformPoint(worldCorners[2]);
            if (max.x <= min.x || max.y <= min.y) continue;
            faces.Add(new Face { Tile = card.tileId, Drawn = card.isDrawSlotPinned, Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y) });
        }
        faces.Sort((a, b) => a.Bounds.xMin.CompareTo(b.Bounds.xMin));
    }

    private int HitFace(Vector2 screen) {
        if (drawerOpen && panel && RectTransformUtility.RectangleContainsScreenPoint(panel, screen, eventCamera)) return -1;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, eventCamera, out var point)) return -1;
        for (int i = 0; i < faces.Count; i++)
            if (faces[i].Bounds.Contains(point)) return i;
        return -1;
    }

    internal bool CapturesHandPress(Vector2 screen) {
        return isActiveAndEnabled && (mode == DisplayMode.C || Input.touchCount > 0)
            && markerLayer.gameObject.activeInHierarchy && node == owner.currentNode && round == owner.currentRoundIndex
            && seat == owner.selectedPlayerOriginalIndex && HitFace(screen) >= 0;
    }

    private int FocusedFace() {
        if (Input.touchCount > 0) {
            var touch = Input.GetTouch(0);
            touchWasLastInput = true;
            lastTouchPosition = touch.position;
            if (Input.touchCount != 1 || touch.phase == TouchPhase.Canceled || touch.phase == TouchPhase.Ended) {
                pressStartedOnHand = false;
                return -1;
            }
            int hit = HitFace(touch.position);
            if (touch.phase == TouchPhase.Began) pressStartedOnHand = hit >= 0;
            return pressStartedOnHand ? hit : -1;
        }
        Vector2 mouse = Input.mousePosition;
        if (touchWasLastInput && (mouse - lastTouchPosition).sqrMagnitude < 4 && !Input.GetMouseButtonDown(0)) return -1;
        touchWasLastInput = false;
        if (!Input.mousePresent) return -1;
        int hover = HitFace(mouse);
        if (mode == DisplayMode.B) return hover;
        if (Input.GetMouseButtonDown(0)) pressStartedOnHand = hover >= 0;
        if (!Input.GetMouseButton(0)) pressStartedOnHand = false;
        return pressStartedOnHand ? hover : -1;
    }

    private void UpdateHandMarkers() {
        CollectFaces();
        int focus = FocusedFace(), used = 0;
        for (int first = 0; first < faces.Count;) {
            int end = first + 1;
            var face = faces[first];
            while (end < faces.Count && faces[end].Tile == face.Tile && !faces[end].Drawn && !face.Drawn
                && faces[end].Bounds.xMin - faces[end - 1].Bounds.xMax < face.Bounds.width * .45f) end++;
            bool focused = focus >= first && focus < end;
            decision.Discards.TryGetValue(face.Tile, out var candidate);
            bool top = candidate != null && candidate.DiscardRank <= 3;
            if (focused || (candidate != null && (mode == DisplayMode.C || top || showAll))) {
                if (used == markers.Count) markers.Add(NewMarker());
                float center = focused ? faces[focus].Bounds.center.x : (face.Bounds.center.x + faces[end - 1].Bounds.center.x) * .5f;
                float topY = focused ? faces[focus].Bounds.yMax : Mathf.Max(face.Bounds.yMax, faces[end - 1].Bounds.yMax);
                DrawMarker(markers[used++], candidate, focused, center, topY, face.Bounds.width,
                    focused ? 0 : faces[end - 1].Bounds.center.x - face.Bounds.center.x);
            }
            first = end;
        }
        for (int i = used; i < markers.Count; i++) markers[i].Root.gameObject.SetActive(false);
    }

    private Marker NewMarker() {
        var marker = new Marker { Root = RectChild(markerLayer, "TileMarker") };
        marker.Root.anchorMin = marker.Root.anchorMax = root.pivot;
        marker.Root.pivot = new Vector2(.5f, 0);
        marker.Badge = RectChild(marker.Root, "Badge");
        Stretch(marker.Badge);
        marker.Surface = Surface(marker.Badge, Slate, Edge, false);
        marker.Bar = RectChild(marker.Root, "ProbabilityBar");
        marker.Bar.anchorMin = marker.Bar.anchorMax = marker.Bar.pivot = new Vector2(.5f, 0);
        marker.BarImage = marker.Bar.gameObject.AddComponent<Image>();
        marker.BarImage.raycastTarget = false;
        marker.Bracket = RectChild(marker.Root, "EqualTiles");
        marker.Bracket.anchorMin = marker.Bracket.anchorMax = new Vector2(.5f, 0);
        marker.Bracket.anchoredPosition = new Vector2(0, -2);
        var line = marker.Bracket.gameObject.AddComponent<Image>();
        line.color = Muted; line.raycastTarget = false;
        marker.Label = Label(marker.Root, "Percent", "", 23, Paper, TextAlignmentOptions.Center);
        marker.Label.enableAutoSizing = true;
        return marker;
    }

    private void DrawMarker(Marker mark, RiichiReviewReport.Candidate candidate, bool focus, float x, float y,
        float width, float duplicateSpan) {
        bool tag = focus || (mode == DisplayMode.B && candidate != null && candidate.DiscardRank <= 3);
        bool bar = mode == DisplayMode.C && !focus;
        bool best = candidate != null && candidate.Key == decision.Recommended;
        mark.Root.gameObject.SetActive(true);
        float tagWidth = Mathf.Clamp(width * .98f, 26, 108), tagHeight = tagWidth * .37f;
        float space = Mathf.Min(width * .065f, 7);
        mark.Root.anchoredPosition = new Vector2(Mathf.Clamp(x, root.rect.xMin + tagWidth / 2, root.rect.xMax - tagWidth / 2), y + space);
        mark.Root.sizeDelta = new Vector2(tagWidth, tagHeight);
        mark.Badge.gameObject.SetActive(tag);
        mark.Bar.gameObject.SetActive(bar);
        mark.Bracket.gameObject.SetActive(duplicateSpan > 0);
        if (duplicateSpan > 0) mark.Bracket.sizeDelta = new Vector2(duplicateSpan, 1.5f);
        mark.Label.gameObject.SetActive(!bar);
        if (bar) {
            mark.Bar.sizeDelta = new Vector2(width * .20f, Mathf.Max(2, (float)candidate.Probability * width * .82f));
            mark.BarImage.color = best ? Mint : Muted;
            return;
        }
        var fill = best ? Mint : Slate;
        var border = focus ? Gold : best ? Mint : Edge;
        if (mark.Surface.color != fill || mark.Surface.edgeColor != border) {
            mark.Surface.color = mark.Surface.bottomColor = fill;
            mark.Surface.edgeColor = border;
            mark.Surface.SetVerticesDirty();
        }
        mark.Label.color = tag && best ? Ink : focus ? Gold : Paper;
        mark.Label.fontSizeMax = tagHeight * .76f;
        mark.Label.fontSizeMin = tagHeight * .5f;
        mark.Label.text = candidate == null ? "无数据" : tag ? candidate.Exact : candidate.Short;
    }
}
