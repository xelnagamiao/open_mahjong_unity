using Salasasa.Home;
using TMPro;
using UnityEngine;

/// <summary>The sender's copy-link dialog. Receiving an invitation uses normal room entry.</summary>
public sealed class RoomInvitePanel : MonoBehaviour {
    public BakedModalFade fade;
    public TMP_Text title, hint, status, primaryLabel;
    public TMP_InputField shareInput;
    public UnityEngine.UI.Button primary, close;
    public UnityEngine.UI.Button friendInvite;
    public FriendInvitePanel friendPicker;
    RoomInfo info;
    bool busy;

    void Awake() {
        primary.onClick.AddListener(Copy);
        if (friendInvite) friendInvite.onClick.AddListener(() => { if (info != null) { fade.Hide(); friendPicker.ShowRoom(info); } });
        close.onClick.AddListener(() => fade.Hide());
    }
    void OnEnable() { ConfigManager.OnLanguageChanged += RefreshText; RefreshText(); }
    void OnDisable() { ConfigManager.OnLanguageChanged -= RefreshText; CancelInvoke(); }
    public void ShowShare(RoomInfo room) {
        if (room == null) return;
        info = room; busy = false;
        Set(status, ""); fade.Show(); RefreshText();
    }
    public void UpdateSharedRoom(RoomInfo room) {
        if (!gameObject.activeSelf) return;
        if (room == null || info == null || room.instance_id != info.instance_id) { fade.Hide(); return; }
        info = room; RefreshText();
    }
    void RefreshText() {
        Set(title, "邀请好友加入房间");
        string text = info == null ? null : RoomInvitation.ShareText(info, ConfigManager.webUrl);
        shareInput.SetTextWithoutNotify(text ?? "");
        primary.interactable = !busy && text != null;
        if (friendInvite) friendInvite.interactable = info != null && !info.is_game_running;
        Set(primaryLabel, busy ? "请稍候…" : "复制邀请");
        Set(hint, info != null && info.has_password ? "房间解散后链接失效；加入时仍需输入房间密码。" : "房间解散后，邀请链接失效。");
    }
    void Copy() {
        if (busy || info == null) return;
        busy = true; RefreshText();
        Invoke(nameof(CopyTimedOut), 8);
        ClipboardUtility.CopyWithResult(shareInput.text, gameObject, nameof(OnClipboardResult));
    }
    public void OnClipboardResult(string result) {
        CancelInvoke(nameof(CopyTimedOut));
        if (!isActiveAndEnabled) return;
        busy = false; RefreshText();
        Set(status, result == "ok" ? "已复制，发送给好友即可。" : "复制失败，请选择上方文字手动复制。");
    }
    void CopyTimedOut() => OnClipboardResult("failed");
    static void Set(TMP_Text label, string value) {
        if (!label) return;
        var localized = label.GetComponent<LocalizedPlatformLabel>();
        if (localized) localized.SetSource(value); else label.text = PlatformText.Get(value);
        label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
    }
}
