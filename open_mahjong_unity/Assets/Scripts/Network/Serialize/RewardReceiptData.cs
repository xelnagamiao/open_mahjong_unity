using System;

[Serializable] public sealed class RewardItem : InventoryItem {
    public int quantity, quantity_after;
}
[Serializable] public sealed class RewardReceipt {
    public int version, user_id;
    public string receipt_id, source, source_id;
    public RewardItem[] items, skipped_items;
}
[Serializable] public sealed class RedeemCommand {
    public string type = "reward/redeem", request_id, code;
}
