using System;
using System.Globalization;
using TMPro;
using UnityEngine;

public sealed class ShopWalletRow : MonoBehaviour {
    public TMP_Text date, source, amount, balance;
    public void Bind(WalletEntry entry) {
        date.text = DateTimeOffset.TryParse(entry.created_at, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed.ToLocalTime().ToString("MM-dd HH:mm") : "";
        source.text = entry.source == "game_reward"
            ? PlatformText.Format(entry.metadata != null && entry.metadata.backfill ? "历史奖励补发 · {0} 小局" : "对局奖励 · {0} 小局", entry.completed_hands ?? 0)
            : PlatformText.Get(entry.source == "purchase" ? "商店购买"
                : entry.source == "game_reward_recalculation" ? "奖励重算 · 仅匹配场" : "萨拉币调整");
        amount.text = (entry.delta_minor > 0 ? "+" : "") + ShopPanel.Money(entry.delta_minor);
        amount.color = entry.delta_minor > 0 ? new Color(.73f,.86f,.73f) : new Color(.94f,.73f,.61f);
        balance.text = ShopPanel.Money(entry.balance_after);
    }
}
