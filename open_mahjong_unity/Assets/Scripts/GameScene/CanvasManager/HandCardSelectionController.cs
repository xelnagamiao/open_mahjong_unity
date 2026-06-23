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
            // 必须同时落下立起视觉再清空：出牌请求发出后该牌通常会被服务端回包销毁，
            // 但若销毁延迟（疯狂点击/网络慢），仅清空 armedCard 会让这张牌一直悬浮，
            // 且因 armedCard 已为空，立起其它牌也无法把它落下（表现为需点两下才能再出）。
            SetArmedVisual(armedCard, false);
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
        // 立起后同步显示该牌的听牌提示（确认出牌模式下悬停不弹起，提示改由立起驱动）
        card.OnArmedStateChanged(armed);
    }
}
