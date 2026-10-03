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
        bool grade=RankedRules.IsGrade(entry.rule);
        var rating=new RuleRating{rule=entry.rule,rank_name=entry.rank_name??entry.guobiao_rank,rank_score=entry.rank_name==null?entry.guobiao_score:entry.rank_score,elo=entry.elo,games=entry.games};
        if(rankNameText!=null)rankNameText.text=grade?rating.rank_name:$"R {rating.elo:0.##}";
        if(scoreText!=null)scoreText.text=grade?$"{rating.rank_score:0.##} PT  ·  R {rating.elo:0.##}":$"{rating.games} 场 Elo 对局";
        LoadAvatar(entry.profile_image_id);

        UpdateAvatarClickTarget(entry.user_id);
        if(avatar&&avatar.TryGetComponent<ProfileOnClick>(out var click))click.ratingRule=entry.rule;
    }

    private void UpdateAvatarClickTarget(int uid) {
        if (avatar == null) return;
        var click = avatar.gameObject.GetComponent<ProfileOnClick>();
        if (click != null) click.user_id = uid;
    }

    private void LoadAvatar(int profileImageId) {
        if (avatar == null) return;
        Sprite sprite = ConfigManager.GetProfileSprite(profileImageId);
        if (sprite != null) avatar.sprite = sprite;
    }
}
