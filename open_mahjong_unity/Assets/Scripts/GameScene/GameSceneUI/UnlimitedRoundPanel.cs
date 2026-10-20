using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Displays the authoritative end-after-hand requests for Qingque custom games.</summary>
public partial class UnlimitedRoundPanel : MonoBehaviour {
    public static UnlimitedRoundPanel Instance { get; private set; }

    [SerializeField] private GameObject noticeRoot;
    [SerializeField] private UnityEngine.UI.Image background;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private Color withdrawnColor = new Color32(155, 42, 52, 245);
    [SerializeField] private Color returnedColor = new Color32(39, 85, 159, 245);

    private UnlimitedRoundInfo state;
    private float remaining;
    private int shownSeconds = -1;
    private bool canControl;

    private void Awake() {
        Instance = this;
        if (noticeRoot) noticeRoot.SetActive(false);
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    public void Initialize(GameInfo info, bool isPlayer) {
        state = null;
        canControl = isPlayer && info.room_rule == "qingque" && info.room_type == "custom" && info.unlimited_rounds;
        ApplyState(info.unlimited_rounds ? info.unlimited_info : null);
    }

    public void ApplyState(UnlimitedRoundInfo info) {
        if (info != null && state != null && info.revision < state.revision) return;
        state = info;
        remaining = Mathf.Max(0, info?.countdown ?? 0);
        shownSeconds = -1;
        bool enabled = info != null && info.enabled;
        bool withdrawn = enabled && info.withdrawn_players != null
            && info.withdrawn_players.Exists(p => p.user_id == UserDataManager.Instance.UserId);
        ExitButtonManager.Instance?.ConfigureUnlimitedRounds(enabled && canControl, withdrawn);
        RefreshNotice();
    }

    private void Update() {
        if (remaining <= 0) return;
        remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);
        if (remaining <= 0) RefreshNotice();
        else RefreshCountdown();
    }

    private void RefreshNotice() {
        bool returned = state?.enabled == true && state.returned_player != null && remaining > 0;
        bool withdrawn = state?.enabled == true && state.withdrawn_players != null && state.withdrawn_players.Count > 0;
        noticeRoot.SetActive(returned || withdrawn);
        if (!returned && !withdrawn) return;
        background.color = returned ? returnedColor : withdrawnColor;
        messageText.richText = false;
        messageText.text = returned
            ? PlayerName(state.returned_player) + "玩家已回归游戏"
            : string.Join("、", state.withdrawn_players.ConvertAll(PlayerName)) + "玩家已放弃游戏，游戏将在本局游戏后结束";
        countdownText.gameObject.SetActive(returned);
        if (returned) RefreshCountdown();
    }

    private void RefreshCountdown() {
        int seconds = Mathf.CeilToInt(remaining);
        if (seconds == shownSeconds) return;
        shownSeconds = seconds;
        countdownText.text = $"{seconds}秒";
    }

    private static string PlayerName(UnlimitedRoundPlayer player) {
        var manager = NormalGameStateManager.Instance;
        if (manager != null && manager.player_to_info != null) {
            foreach (var entry in manager.player_to_info) {
                if (entry.Value.userId == player.user_id)
                    return StreamerModeHelper.FormatGamestatePlayerName(player.username, entry.Key, player.user_id);
            }
        }
        return player.username ?? $"用户{player.user_id}";
    }

    public void ResetState() {
        state = null;
        remaining = 0;
        canControl = false;
        if (noticeRoot) noticeRoot.SetActive(false);
        ExitButtonManager.Instance?.ConfigureUnlimitedRounds(false, false);
    }
}
