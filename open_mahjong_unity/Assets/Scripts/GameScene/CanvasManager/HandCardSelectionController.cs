using UnityEngine;

/// <summary>
/// 两次点击确认出牌：维护当前「已选中待打出」的手牌，并驱动选中上浮。
/// </summary>
public class HandCardSelectionController : MonoBehaviour {
    public static HandCardSelectionController Instance { get; private set; }

    private TileCard armedCard;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy() {
        if (Instance == this) {
            Instance = null;
        }
    }

    public bool IsArmed(TileCard card) {
        return armedCard == card;
    }

    public void Arm(TileCard card) {
        if (card == null || !card.IsSelectableForCut()) {
            return;
        }
        if (armedCard == card) {
            return;
        }
        SetArmedVisual(armedCard, false);
        armedCard = card;
        SetArmedVisual(armedCard, true);
    }

    public void CommitDiscard(TileCard card) {
        if (card == null) {
            DisarmAll();
            return;
        }
        if (armedCard == card) {
            armedCard = null;
            return;
        }
        DisarmAll();
    }

    public void DisarmAll() {
        if (armedCard == null) {
            return;
        }
        SetArmedVisual(armedCard, false);
        armedCard = null;
    }

    public void OnCardDestroyed(TileCard card) {
        if (armedCard == card) {
            armedCard = null;
        }
    }

    private static void SetArmedVisual(TileCard card, bool armed) {
        if (card == null) {
            return;
        }
        HoverEventTrigger hover = card.GetComponent<HoverEventTrigger>();
        if (hover != null) {
            hover.SetArmedLift(armed);
        }
    }
}
