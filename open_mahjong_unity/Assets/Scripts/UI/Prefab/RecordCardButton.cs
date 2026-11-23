using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A pooled button can be under the pointer again after a wheel jump, showing a different record.
public sealed class RecordCardButton : Button {
    private RecordPrefab card;
    private Dictionary<int, string> presses;
    private string releasedRecord;

    public override void OnPointerDown(PointerEventData eventData) {
        base.OnPointerDown(eventData);
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (!card) card = GetComponentInParent<RecordPrefab>();
        if (!card) return;
        presses ??= new Dictionary<int, string>();
        presses[eventData.pointerId] = card.GameId;
    }

    public override void OnPointerUp(PointerEventData eventData) {
        base.OnPointerUp(eventData);
        releasedRecord = null;
        if (presses != null && eventData.button == PointerEventData.InputButton.Left &&
            presses.TryGetValue(eventData.pointerId, out releasedRecord)) {
            presses.Remove(eventData.pointerId);
        }
    }

    public override void OnPointerClick(PointerEventData eventData) {
        if (releasedRecord != null && card && releasedRecord == card.GameId)
            base.OnPointerClick(eventData);
        releasedRecord = null;
    }

    protected override void OnDisable() {
        presses?.Clear();
        releasedRecord = null;
        base.OnDisable();
    }
}
