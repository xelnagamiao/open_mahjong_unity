using System.Collections.Generic;
using UnityEngine;

public sealed partial class CardFaceFamilyPreview
{
    readonly VisibleGallery[] galleries = new VisibleGallery[4];
    readonly Vector3[] viewportCorners = new Vector3[4];

    void OnPreviewScrolled(Vector2 _) => UpdateVisibleTiles(false);

    void UpdateVisibleTiles(bool force)
    {
        if (family == 0 || !hongqueHandTemplate || !isActiveAndEnabled) return;
        readingArea.scroll.viewport.GetWorldCorners(viewportCorners);
        bool hongque = family == 1;
        var entries = hongque ? hongqueEntries : expandedEntries;
        int index = hongque ? 0 : 2;
        var hand = galleries[index] ??= new VisibleGallery(hongque ? hongqueHandTemplate : expandedHandTemplate, false);
        var table = galleries[index + 1] ??= new VisibleGallery(hongque ? hongqueTableTemplate : expandedTableTemplate, true);
        hand.Draw(entries, editingPack, viewportCorners, force);
        table.Draw(entries, editingPack, viewportCorners, force);
    }

    // The baked positions include section headers and gaps in the expanded tile gallery.
    sealed class VisibleGallery
    {
        readonly CardFacePreviewSlot template;
        readonly RectTransform container;
        readonly bool table;
        readonly Stack<CardFacePreviewSlot> unused = new Stack<CardFacePreviewSlot>();
        readonly Dictionary<int, CardFacePreviewSlot> visible = new Dictionary<int, CardFacePreviewSlot>();
        readonly List<int> leaving = new List<int>();

        public VisibleGallery(CardFacePreviewSlot template, bool table)
        {
            this.template = template;
            this.table = table;
            container = (RectTransform)template.transform.parent;
        }

        public void Draw(Entry[] entries, string pack, Vector3[] corners, bool force)
        {
            float height = ((RectTransform)template.transform).rect.height;
            float top = container.InverseTransformPoint(corners[1]).y - container.rect.yMax;
            float bottom = container.InverseTransformPoint(corners[0]).y - container.rect.yMax;
            leaving.Clear();
            foreach (var pair in visible)
            {
                float y = Position(entries[pair.Key]).y;
                if (y < bottom || y - height > top) leaving.Add(pair.Key);
            }
            foreach (int index in leaving)
            {
                Recycle(visible[index]);
                visible.Remove(index);
            }
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var position = Position(entry);
                if (position.y < bottom || position.y - height > top) continue;
                if (!visible.TryGetValue(i, out var slot))
                {
                    slot = unused.Count > 0 ? unused.Pop() : Object.Instantiate(template, container, false);
                    visible.Add(i, slot);
                    ((RectTransform)slot.transform).anchoredPosition = position;
                    var trigger = slot.GetComponent<CardFacePreviewMagnifierTrigger>();
                    trigger.fixedPack = pack;
                    trigger.fixedTileKey = FixedFacePack.Key(entry.source, entry.id);
                }
                else if (!force) continue;
                FixedFacePack.ApplyPreview(slot, pack, FixedFacePack.Key(entry.source, entry.id), table);
                slot.gameObject.SetActive(true);
            }
        }

        public void Clear()
        {
            foreach (var slot in visible.Values) Recycle(slot);
            visible.Clear();
        }

        void Recycle(CardFacePreviewSlot slot)
        {
            slot.gameObject.SetActive(false);
            slot.ClearArtwork();
            unused.Push(slot);
        }

        Vector2 Position(Entry entry) => table ? entry.tablePosition : entry.handPosition;
    }
}
