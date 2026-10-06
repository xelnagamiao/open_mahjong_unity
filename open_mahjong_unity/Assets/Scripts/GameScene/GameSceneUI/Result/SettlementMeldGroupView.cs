using System.Collections.Generic;
using UnityEngine;

/// <summary>一副吃碰杠。牌体整张旋转，横牌和加杠共享一个列宽。</summary>
public class SettlementMeldGroupView : UnityEngine.UI.LayoutGroup {
    private sealed class Slot {
        public RectTransform Base;
        public RectTransform Added;
        public bool Sideways;
        public List<Overlay> Stacks;
        public Vector2 BaseSize => SizeOf(Base, Sideways);
        public Vector2 AddedSize => Added == null ? Vector2.zero : SizeOf(Added, true);
        public float Width => Mathf.Max(BaseSize.x, AddedSize.x);
        public float Height {
            get {
                float height = BaseSize.y + BaseStackHeight + AddedSize.y;
                if (Stacks == null) return height;
                foreach (var stack in Stacks) height = Mathf.Max(height, StackY(stack) + SizeOf(stack.Card, stack.Sideways).y * .5f);
                return height;
            }
        }
        public float BaseStackHeight {
            get {
                float height = 0f;
                if (Stacks != null) foreach (var stack in Stacks)
                    if (!stack.OnAddedKong) height = Mathf.Max(height, SizeOf(stack.Card, stack.Sideways).y * stack.Layer);
                return height;
            }
        }
        public float StackY(Overlay stack) {
            float tileHeight = SizeOf(stack.Card, stack.Sideways).y;
            return BaseSize.y + (stack.OnAddedKong ? BaseStackHeight + AddedSize.y : 0f)
                + tileHeight * (stack.Layer - .5f);
        }
    }

    private sealed class Overlay {
        public RectTransform Card;
        public bool Sideways;
        public int Layer;
        public bool OnAddedKong;
    }

    private readonly List<Slot> slots = new List<Slot>();

    public static SettlementMeldGroupView Create(Transform parent, GameObject cardPrefab,
        List<SettlementMeldLayoutBuilder.TileSlot> tiles) {
        var go = new GameObject("SettlementMeld", typeof(RectTransform), typeof(SettlementMeldGroupView));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var view = go.GetComponent<SettlementMeldGroupView>();
        foreach (var tile in tiles) {
            var slot = new Slot {
                Base = CreateCard(go.transform, cardPrefab, tile.FaceDown ? 0 : tile.TileId),
                Sideways = tile.Sideways,
            };
            if (tile.StackedTileId.HasValue) slot.Added = CreateCard(go.transform, cardPrefab, tile.StackedTileId.Value);
            if (tile.StackedTiles != null) foreach (var stack in tile.StackedTiles) {
                if (slot.Stacks == null) slot.Stacks = new List<Overlay>();
                slot.Stacks.Add(new Overlay {
                    Card = CreateCard(go.transform, cardPrefab, stack.FaceDown ? 0 : stack.TileId),
                    Sideways = stack.OnAddedKong || tile.Sideways, Layer = stack.Layer,
                    OnAddedKong = stack.OnAddedKong,
                });
            }
            view.slots.Add(slot);
        }
        view.CalculateLayoutInputHorizontal();
        view.CalculateLayoutInputVertical();
        view.rectTransform.sizeDelta = new Vector2(view.preferredWidth, view.preferredHeight);
        view.SetDirty();
        return view;
    }

    private static RectTransform CreateCard(Transform parent, GameObject prefab, int tileId) {
        GameObject card = Instantiate(prefab, parent, false);
        card.GetComponent<StaticCard>().SetTileOnlyImage(tileId);
        return (RectTransform)card.transform;
    }

    private static Vector2 SizeOf(RectTransform card, bool sideways) {
        Vector2 size = card.rect.size;
        return sideways ? new Vector2(size.y, size.x) : size;
    }

    public override void CalculateLayoutInputHorizontal() {
        base.CalculateLayoutInputHorizontal();
        float width = 0f;
        foreach (var slot in slots) width += slot.Width;
        SetLayoutInputForAxis(width, width, 0f, 0);
    }

    public override void CalculateLayoutInputVertical() {
        float height = 0f;
        foreach (var slot in slots) height = Mathf.Max(height, slot.Height);
        SetLayoutInputForAxis(height, height, 0f, 1);
    }

    public override void SetLayoutHorizontal() { Arrange(); }
    public override void SetLayoutVertical() { Arrange(); }

    private void Arrange() {
        float x = 0f;
        foreach (var slot in slots) {
            Place(slot.Base, slot.Sideways, x + slot.Width * .5f, slot.BaseSize.y * .5f);
            if (slot.Added != null) {
                Place(slot.Added, true, x + slot.Width * .5f, slot.BaseSize.y + slot.BaseStackHeight + slot.AddedSize.y * .5f);
            }
            if (slot.Stacks != null) foreach (var stack in slot.Stacks)
                Place(stack.Card, stack.Sideways, x + slot.Width * .5f, slot.StackY(stack));
            x += slot.Width;
        }
    }

    private void Place(RectTransform card, bool sideways, float x, float y) {
        m_Tracker.Add(this, card, DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
            | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.Rotation
            | DrivenTransformProperties.Scale);
        card.anchorMin = card.anchorMax = Vector2.zero;
        card.pivot = new Vector2(.5f, .5f);
        card.localScale = Vector3.one;
        // CSS rotate(-90deg) 的屏幕坐标方向对应 Unity 的 +90deg。
        card.localRotation = Quaternion.Euler(0f, 0f, sideways ? 90f : 0f);
        card.anchoredPosition = new Vector2(x, y);
    }
}
