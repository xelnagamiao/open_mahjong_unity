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
    [SerializeField] private TextMeshProUGUI MatchOutcomeText;
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
    [SerializeField] private Button Copy2DLinkButton;
    [SerializeField] private Button Copy3DLinkButton;
    [SerializeField] private Button FavoriteButton;

    private string gameId;
    private PlayerRecordInfo[] playersInfo;
    private bool isFavorite;
    private bool favoriteRequestPending;
    private bool localPlayback;
    [SerializeField] private GameObject[] additionalRows;
    [SerializeField] private TextMeshProUGUI[] additionalRanks, additionalNames, additionalScores;
    private readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> originalPlayerRects = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();
    private readonly System.Collections.Generic.Dictionary<TMP_Text, TextAlignmentOptions> originalPlayerAlignment = new System.Collections.Generic.Dictionary<TMP_Text, TextAlignmentOptions>();
    private bool pairedLayoutActive, playerLayoutDirty;

    public string GameId => gameId;

    public void InitializeRecordItem(
        string gameId,
        string subRule,
        string matchType,
        string recordedTime,
        PlayerRecordInfo[] players,
        bool isFavorite = false,
        bool localPlayback = false,
        Newtonsoft.Json.Linq.JObject matchSummary = null
    ) {
        BindRecord(new RecordInfo {
            game_id = gameId, sub_rule = subRule, match_type = matchType, created_at = recordedTime,
            players = players, is_favorite = isFavorite, match_summary = matchSummary
        }, localPlayback);
    }

    public void BindRecord(RecordInfo record, bool localPlayback = false) {
        gameId = record.game_id;
        var players = record.players;
        var matchSummary = record.match_summary;
        this.playersInfo = players;
        isFavorite = record.is_favorite;
        this.favoriteRequestPending = false;
        this.localPlayback = localPlayback;

        RecordIdText.text = gameId;
        var keys = RuleNameDictionary.RecordKeys(record.rule, record.sub_rule, matchSummary);
        string subRule = keys.subRule;
        string ruleName = RuleNameDictionary.GetRecordName(keys.rule, subRule);
        string matchTypeDisplay = RoundTextDictionary.GetMatchTypeDisplay(subRule, record.match_type ?? "");
        if (RuleText.TryGetComponent<LocalizedPlatformLabel>(out var ruleBinding)) ruleBinding.SetSource(ruleName);
        else RuleText.text = PlatformText.Get(ruleName);
        MatchTypeText.text = matchTypeDisplay;
        RecordedTimeText.text = record.created_at;

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
                    rankTexts[i].text = subRule == "sichuan/doubles_duplicate" || subRule == "sichuan/xueliu_doubles_duplicate"
                        ? "—" : $"{players[i].rank}位";
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
        ApplyLocalPlaybackChrome();
        ApplyMatchSummary(matchSummary);
    }

    private void ApplyMatchSummary(Newtonsoft.Json.Linq.JObject summary) {
        bool paired = summary?["tables"]?["closed"] != null && additionalRows != null && additionalRows.Length == 2;
        if (additionalRows == null || additionalRows.Length != 2) return;
        foreach (var row in additionalRows) row.SetActive(paired);
        float height = 210;
        var rect = (RectTransform)transform;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        var layout = GetComponent<LayoutElement>();
        if (layout != null) layout.minHeight = layout.preferredHeight = height;
        var body = (RectTransform)transform.Find("Container");
        body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height - 66);
        var divider = body.Find("Players/PlayerColumnDivider") as RectTransform;
        if (divider != null) divider.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 112);
        pairedLayoutActive = paired;
        if (MatchOutcomeText != null) {
            MatchOutcomeText.gameObject.SetActive(paired);
            string outcome = paired ? MatchRecordCard.ResolveMatchOutcome(summary) : "";
            string caption = paired ? MatchRecordCard.ResolveMatchOutcomeHeader(summary) : "";
            var localized = MatchOutcomeText.GetComponent<LocalizedPlatformLabel>();
            if (localized != null) localized.SetSource(caption);
            else MatchOutcomeText.text = caption;
            MatchOutcomeText.color = outcome == "蓝胜" ? new Color32(128,194,255,255)
                : outcome == "红胜" ? new Color32(255,152,158,255) : Color.white;
        }
        if (!paired) LayoutPlayerRows(false);
        if (!paired) return;
        var ranks = new[] {Rank1Text,Rank2Text,Rank3Text,Rank4Text};
        var names = new[] {Username1Text,Username2Text,Username3Text,Username4Text};
        var scores = new[] {Score1Text,Score2Text,Score3Text,Score4Text};
        var personalImp = summary?["personal_imp"] as Newtonsoft.Json.Linq.JObject ?? PersonalImpSummary.Build(summary);
        for (int table=0;table<2;table++) {
            string id=table==0?"open":"closed";
            var players=summary["tables"]?[id]?["players"]?.ToObject<PlayerRecordInfo[]>() ?? System.Array.Empty<PlayerRecordInfo>();
            for(int seat=0;seat<4;seat++) {
                var player=System.Array.Find(players,p=>p!=null && p.original_player_index==seat);
                var rank=table==0?ranks[seat]:additionalRanks[seat];
                var name=table==0?names[seat]:additionalNames[seat];
                var score=table==0?scores[seat]:additionalScores[seat];
                rank.text="";
                name.richText=false; name.text=player?.username ?? "暂无数据";
                score.text=player==null?"—":PersonalImpSummary.ScoreText(personalImp,id,seat);
                int? team=MatchRecordCard.ResolveTeam(summary,id,seat);
                var color=team==0?(Color)new Color32(35,108,192,255):team==1?(Color)new Color32(198,45,59,255):new Color32(83,94,110,255);
                name.color=score.color=color;
            }
        }
        LayoutPlayerRows(true);
    }

    private void OnRectTransformDimensionsChange() => playerLayoutDirty = true;
    private void OnEnable() { PlatformTypography.Changed += MarkPlayerLayoutDirty; ConfigManager.OnLanguageChanged += MarkPlayerLayoutDirty; }
    private void OnDisable() { PlatformTypography.Changed -= MarkPlayerLayoutDirty; ConfigManager.OnLanguageChanged -= MarkPlayerLayoutDirty; }
    private void MarkPlayerLayoutDirty() => playerLayoutDirty = true;
    private void LateUpdate() {
        if (pairedLayoutActive && playerLayoutDirty) LayoutPlayerRows(true);
    }

    private void LayoutPlayerRows(bool paired) {
        var ranks = new[] {Rank1Text,Rank2Text,Rank3Text,Rank4Text};
        var names = new[] {Username1Text,Username2Text,Username3Text,Username4Text};
        var scores = new[] {Score1Text,Score2Text,Score3Text,Score4Text};
        void Remember(RectTransform r) {
            if (!originalPlayerRects.ContainsKey(r)) originalPlayerRects[r] = new[] {r.anchorMin,r.anchorMax,r.anchoredPosition,r.sizeDelta};
        }
        for (int table=0;table<2;table++) for (int seat=0;seat<4;seat++) {
            var rank=(table==0?ranks[seat]:additionalRanks[seat]).rectTransform;
            var name=(table==0?names[seat]:additionalNames[seat]).rectTransform;
            var score=(table==0?scores[seat]:additionalScores[seat]).rectTransform;
            var row=(RectTransform)name.parent;
            foreach(var r in new[]{row,rank,name,score})Remember(r);
            foreach(var r in new[]{rank,name,score}) {
                var text=r.GetComponent<TMP_Text>();
                if(!originalPlayerAlignment.ContainsKey(text))originalPlayerAlignment[text]=text.alignment;
            }
            rank.gameObject.SetActive(!paired);
            if (!paired) continue;
            float half=table*.5f;
            row.anchorMin=new Vector2(half,1);row.anchorMax=new Vector2(half+.5f,1);
            row.anchoredPosition=new Vector2(0,seat<2?-18:-82);row.sizeDelta=new Vector2(0,44);
            float left=(seat%2)*.5f;
            var nameText=name.GetComponent<TMP_Text>();
            var scoreText=score.GetComponent<TMP_Text>();
            float scoreWidth=scoreText.GetPreferredValues(scoreText.text).x;
            float leftInset=seat%2==0?32:10;
            float rightInset=seat%2==0?10:34;
            const float gap=10;
            float scoreX=row.rect.width*.5f-rightInset-scoreWidth;
            float nameWidth=Mathf.Max(0,scoreX-leftInset-gap);
            void Place(RectTransform r,TMP_Text text,float x,float width,TextAlignmentOptions alignment) {
                r.anchorMin=r.anchorMax=new Vector2(left,1);
                r.anchoredPosition=new Vector2(x,0);r.sizeDelta=new Vector2(width,44);
                text.alignment=alignment;
            }
            Place(name,nameText,leftInset,nameWidth,TextAlignmentOptions.MidlineLeft);
            Place(score,scoreText,scoreX,scoreWidth,TextAlignmentOptions.MidlineRight);
        }
        if (!paired) foreach(var entry in originalPlayerRects) {
            var r=entry.Key;var v=entry.Value;r.anchorMin=v[0];r.anchorMax=v[1];r.anchoredPosition=v[2];r.sizeDelta=v[3];
        }
        if(!paired)foreach(var entry in originalPlayerAlignment)entry.Key.alignment=entry.Value;
        playerLayoutDirty=false;
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
        if (Copy2DLinkButton != null) {
            Copy2DLinkButton.onClick.AddListener(Copy2DRecordLink);
        }
        if (Copy3DLinkButton != null) {
            Copy3DLinkButton.onClick.AddListener(Copy3DRecordLink);
        }
        FavoriteButton.onClick.AddListener(ToggleFavorite);
    }

    private void LoadRecord() {
        if (localPlayback) {
            LocalRecordStore.LoadAsync(gameId, local => {
                if (local != null && local.record != null) {
                    RecordPanel.OpenRecord(local, localPlayback: true);
                    return;
                }
                NotificationManager.Instance.ShowTip("牌谱", false, "本机没有这份牌谱");
            });
            return;
        }
        DataNetworkManager.Instance.GetRecordById(gameId);
    }

    private void CopyRecordId() {
        ClipboardUtility.CopyAndNotify(gameId, "牌谱", $"已复制牌谱ID: {gameId}");
    }

    private void Copy2DRecordLink() {
        if (localPlayback) return;
        string shareUrl = $"{ConfigManager.webUrl}/2d/record/{gameId}";
        ClipboardUtility.CopyAndNotify(shareUrl, "牌谱", "已复制 2D 牌谱链接");
    }

    private void Copy3DRecordLink() {
        if (localPlayback) return;
        string shareUrl = SharedRecordLink.BuildShareUrl(gameId);
        ClipboardUtility.CopyAndNotify(shareUrl, "牌谱", "已复制 3D 牌谱链接");
    }

    private void ToggleFavorite() {
        if (localPlayback || favoriteRequestPending || string.IsNullOrEmpty(gameId)) return;
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

    private void ApplyLocalPlaybackChrome() {
        bool cloud = !localPlayback;
        if (Copy2DLinkButton != null) Copy2DLinkButton.gameObject.SetActive(cloud);
        if (Copy3DLinkButton != null) Copy3DLinkButton.gameObject.SetActive(cloud);
        FavoriteButton.gameObject.SetActive(cloud);
    }
}
