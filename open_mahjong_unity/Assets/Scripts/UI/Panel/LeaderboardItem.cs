using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 排行榜列表条目。头像点击复用 ProfileOnClick 打开玩家信息面板。
/// </summary>
public class LeaderboardItem : MonoBehaviour {
    [Header("展示")]
    [SerializeField] private Image avatar;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private TMP_Text uidText;
    [SerializeField] private TMP_Text rankNameText;
    [SerializeField] private TMP_Text scoreText;

    public void Bind(LeaderboardEntry entry) {
        if (entry == null) return;

        if (rankText != null) rankText.text = entry.rank_position.ToString();
        if (usernameText != null) usernameText.text = entry.username ?? "";
        if (uidText != null) uidText.text = $"UID: {entry.user_id}";
        if (rankNameText != null) rankNameText.text = entry.guobiao_rank ?? "";
        if (scoreText != null) {
            string rank = entry.guobiao_rank ?? "10级";
            int idx = RankConfig.GetRankIndex(rank);
            var (_, _, promoteScore) = RankConfig.RankTable[idx];
            scoreText.text = $"{entry.guobiao_score:F1}/{promoteScore}";
        }

        LoadAvatar(entry.profile_image_id);
        EnsureAvatarClickable();
        UpdateAvatarClickTarget(entry.user_id);
    }

    private void EnsureAvatarClickable() {
        if (avatar == null) return;
        if (avatar.gameObject.GetComponent<ProfileOnClick>() == null) {
            avatar.gameObject.AddComponent<ProfileOnClick>();
        }
        avatar.raycastTarget = true;
    }

    private void UpdateAvatarClickTarget(int uid) {
        if (avatar == null) return;
        var click = avatar.gameObject.GetComponent<ProfileOnClick>();
        if (click != null) click.user_id = uid;
    }

    private void LoadAvatar(int profileImageId) {
        if (avatar == null) return;
        Sprite sprite = Resources.Load<Sprite>($"image/Profiles/{profileImageId}");
        if (sprite != null) avatar.sprite = sprite;
    }
}
