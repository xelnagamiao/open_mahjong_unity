using System;

[Serializable] public sealed class MailReward {
    public int item_id, quantity;
    public string name, icon_path;
}

[Serializable] public sealed class MailEntry {
    public long mail_id;
    public string title, content, sender, created_at, expires_at;
    public bool is_read, claimed, expired;
    public MailReward[] rewards;
}

[Serializable] public sealed class MailCommand {
    public string type, request_id;
    public long mail_id;
}
