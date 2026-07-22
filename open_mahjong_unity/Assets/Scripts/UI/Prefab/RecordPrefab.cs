using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RecordPrefab : MonoBehaviour {
    private static readonly Color FavoriteColor = new Color(1f, 0.55f, 0.1f, 1f);
    private static readonly Color NormalFavoriteColor = Color.white;

    [Header("基本信息")]
    [SerializeField] private TextMeshProUGUI RecordIdText;
    [SerializeField] private TextMeshProUGUI RuleText;
    [SerializeField] private TextMeshProUGUI MatchTypeText;
    [SerializeField] private TextMeshProUGUI RecordedTimeText;

    [Header("排名位次")]
    [SerializeField] private TextMeshProUGUI Rank1Text;
    [SerializeField] private TextMeshProUGUI Rank2Text;
    [SerializeField] private TextMeshProUGUI Rank3Text;
    [SerializeField] private TextMeshProUGUI Rank4Text;

    [Header("玩家名")]
    [SerializeField] private TextMeshProUGUI Username1Text;
    [SerializeField] private TextMeshProUGUI Username2Text;
    [SerializeField] private TextMeshProUGUI Username3Text;
    [SerializeField] private TextMeshProUGUI Username4Text;

    [Header("分数")]
    [SerializeField] private TextMeshProUGUI Score1Text;
    [SerializeField] private TextMeshProUGUI Score2Text;
    [SerializeField] private TextMeshProUGUI Score3Text;
    [SerializeField] private TextMeshProUGUI Score4Text;

    [Header("按钮")]
    [SerializeField] private Button LoadRecordButton;
    [SerializeField] private Button CopyIdButton;
    [SerializeField] private Button FavoriteButton;

    private string gameId;
    private PlayerRecordInfo[] playersInfo;
    private bool isFavorite;
    private bool favoriteRequestPending;

    public string GameId => gameId;

    public void InitializeRecordItem(
        string gameId,
        string subRule,
        string matchType,
        string recordedTime,
        PlayerRecordInfo[] players,
        bool isFavorite = false
    ) {
        this.gameId = gameId;
        this.playersInfo = players;
        this.isFavorite = isFavorite;
        this.favoriteRequestPending = false;

        RecordIdText.text = gameId;
        string ruleName = RuleNameDictionary.GetWholeName(subRule);
        string matchTypeDisplay = RoundTextDictionary.GetMatchTypeDisplay(subRule, matchType);
        RuleText.text = ruleName;
        MatchTypeText.text = matchTypeDisplay;
        RecordedTimeText.text = recordedTime;

        TextMeshProUGUI[] rankTexts = { Rank1Text, Rank2Text, Rank3Text, Rank4Text };
        TextMeshProUGUI[] usernameTexts = { Username1Text, Username2Text, Username3Text, Username4Text };
        TextMeshProUGUI[] scoreTexts = { Score1Text, Score2Text, Score3Text, Score4Text };

        if (players != null) {
            var sortedPlayers = System.Array.FindAll(players, p => p != null);
            System.Array.Sort(sortedPlayers, (a, b) => {
                int rankCmp = a.rank.CompareTo(b.rank);
                if (rankCmp != 0) return rankCmp;
                int origA = a.original_player_index ?? int.MaxValue;
                int origB = b.original_player_index ?? int.MaxValue;
                return origA.CompareTo(origB);
            });
            players = sortedPlayers;
            for (int i = 0; i < 4; i++) {
                if (i < players.Length) {
                    rankTexts[i].text = $"{players[i].rank}位";
                    usernameTexts[i].text = players[i].username;
                    scoreTexts[i].text = players[i].score >= 0 ? $"+{players[i].score}" : players[i].score.ToString();
                } else {
                    rankTexts[i].text = "";
                    usernameTexts[i].text = "";
                    scoreTexts[i].text = "";
                }
            }
        }

        RefreshFavoriteVisual();
    }

    public void ApplyFavoriteResult(bool success, bool newFavorite) {
        favoriteRequestPending = false;
        if (success) {
            isFavorite = newFavorite;
        }
        RefreshFavoriteVisual();
    }

    private void Awake() {
        LoadRecordButton.onClick.AddListener(LoadRecord);
        CopyIdButton.onClick.AddListener(CopyRecordId);
        FavoriteButton.onClick.AddListener(ToggleFavorite);
    }

    private void LoadRecord() {
        DataNetworkManager.Instance.GetRecordById(gameId);
    }

    private void CopyRecordId() {
        ClipboardUtility.Copy(gameId);
        NotificationManager.Instance.ShowTip("牌谱", true, $"已复制牌谱ID: {gameId}");
    }

    private void ToggleFavorite() {
        if (favoriteRequestPending || string.IsNullOrEmpty(gameId)) return;
        favoriteRequestPending = true;
        DataNetworkManager.Instance.UpdateRecordFavorite(gameId, !isFavorite);
    }

    private void RefreshFavoriteVisual() {
        Color color = isFavorite ? FavoriteColor : NormalFavoriteColor;
        var colors = FavoriteButton.colors;
        colors.normalColor = color;
        colors.highlightedColor = color;
        colors.selectedColor = color;
        colors.pressedColor = new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f, color.a);
        FavoriteButton.colors = colors;
        FavoriteButton.targetGraphic.color = color;
    }
}
