using System;

[Serializable] public sealed class WalletState {
    public int user_id, scale = 100, reward_minor_per_hand;
    public long balance_minor, earned_minor, spent_minor, version;
    public string currency, unit;
    public bool registered;
}
[Serializable] public sealed class ShopOffer {
    public string offer_id;
    public InventoryItem item;
    public long unit_price_minor;
    public int price_version, max_purchase_quantity, owned_quantity;
    public bool prevent_duplicate_purchase;
}
[Serializable] public sealed class WalletEntry {
    public long entry_id, delta_minor, balance_after;
    public string source, source_id, created_at;
    public int? completed_hands;
    public WalletEntryMetadata metadata;
}
[Serializable] public sealed class WalletEntryMetadata { public bool backfill; }
[Serializable] public sealed class ShopCommand {
    public string type, request_id, offer_id;
    public int quantity = 1, price_version;
    public long? before_entry_id;
}
