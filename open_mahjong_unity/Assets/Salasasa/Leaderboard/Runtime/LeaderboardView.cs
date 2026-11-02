using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Salasasa.Home;

namespace Salasasa.Leaderboards
{
    public sealed class LeaderboardView : MonoBehaviour
    {
        [Serializable]
        public sealed class EntryRow
        {
            public GameObject root;
            public Button button;
            public TMP_Text position, username, uid, detail, score;
            public UnityEngine.UI.RawImage avatar;
            public SettingsShapeGraphic surface;
            public Color normalFill, ownFill;
            public bool showUnits;
            [NonSerialized] public LeaderboardEntry entry;
            public void Set(LeaderboardEntry data, bool grade, LeaderboardPortrait[] portraits, bool sample, int myId)
            {
                entry = data;
                root.SetActive(data != null);
                if (data == null) return;
                position.text = data.rank_position.ToString("00");
                username.text = data.username;
                uid.text = "UID " + data.user_id;
                detail.text = LeaderboardRules.Detail(data, grade);
                score.text = LeaderboardRules.Score(data, grade) + (showUnits ? grade ? " PT" : " R" : "");
                ApplyDefaultPortrait(portraits, avatar);
                surface.color = data.user_id == myId ? ownFill : normalFill;
            }
        }

        public EntryRow[] rows, featured;
        public Button close, retry, myPlayer;
        public TMP_Text subtitle, scoreHeader, detailHeader, source, listCount;
        public TMP_Text myPosition, myName, myGrade, myScore, myHint;
        public UnityEngine.UI.RawImage myAvatar;
        public GameObject listArea, featuredArea, stateArea;
        public TMP_Text stateTitle, stateDescription;
        public RectTransform loadingIcon;
        public ScrollRect scroll;
        public int firstListIndex;
        public TMP_Text featureMetricLabel;
        bool loading;

        public void Bind(LeaderboardController owner)
        {
            close.onClick.AddListener(owner.Close);
            retry.onClick.AddListener(owner.Refresh);
            if (myPlayer) myPlayer.onClick.AddListener(owner.ShowOwnPlayer);
            foreach (var row in rows) BindRow(row, owner);
            foreach (var row in featured) BindRow(row, owner);
        }

        static void BindRow(EntryRow row, LeaderboardController owner)
        {
            row.button.onClick.AddListener(() => { if (row.entry != null) owner.ShowPlayer(row.entry); });
        }

        public void SetRule(int index)
        {
            bool grade = LeaderboardRules.IsGrade(index);
            scoreHeader.text = grade ? "PT" : "R 值";
            detailHeader.text = grade ? "段位" : "Elo 对局";
            if (featureMetricLabel) featureMetricLabel.text = grade ? "PT" : "R 值";
            subtitle.text = "";
        }

        string myNameSource;
        bool myNameIsPlaceholder;
        void OnEnable() { ConfigManager.OnLanguageChanged += RefreshMyName; RefreshMyName(); }
        void OnDisable() { ConfigManager.OnLanguageChanged -= RefreshMyName; }
        void RefreshMyName() { if (myName && myNameIsPlaceholder) myName.text = PlatformText.Get(myNameSource); }

        public void SetRows(LeaderboardEntry[] entries, int rule, LeaderboardPortrait[] portraits, bool sample, int myId, string sourceText)
        {
            SetState(null, null, false, false);
            bool grade = LeaderboardRules.IsGrade(rule);
            for (int i = 0; i < featured.Length; i++) featured[i].Set(i < entries.Length ? entries[i] : null, grade, portraits, sample, myId);
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i + firstListIndex;
                rows[i].Set(index < entries.Length ? entries[index] : null, grade, portraits, sample, myId);
            }
            source.text = "";
            listCount.text = "";
            scroll.StopMovement();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1;
            SetMine(entries, grade, portraits, sample, myId);
        }

        public void SetMine(LeaderboardEntry[] entries, bool grade, LeaderboardPortrait[] portraits, bool sample, int myId, bool rankingResolved = true)
        {
            LeaderboardEntry mine = Array.Find(entries, x => x.user_id == myId);
            ApplyDefaultPortrait(portraits, myAvatar);
            myNameSource = mine != null ? mine.username : sample ? "SALASASA" : myId > 0 ? "我的排名" : "未登录";
            myNameIsPlaceholder = mine == null && !sample;
            myName.text = myNameIsPlaceholder ? PlatformText.Get(myNameSource) : myNameSource;
            myPosition.text = mine != null ? mine.rank_position.ToString("00") : myId > 0 && rankingResolved ? "999+" : "—";
            myGrade.text = mine == null ? "—" : LeaderboardRules.Detail(mine, grade);
            myScore.text = mine == null ? "—" : LeaderboardRules.Score(mine, grade) + (grade ? " PT" : " R");
            myHint.text = mine != null ? "UID " + mine.user_id : myId <= 0 ? "登录后显示我的排名" : rankingResolved ? "未进入当前榜单" : "";
        }

        static void ApplyDefaultPortrait(LeaderboardPortrait[] portraits, RawImage image)
        {
            if (portraits != null && portraits.Length > 0)
                portraits[portraits.Length - 1]?.Apply(image);
        }

        public void SetState(string title, string description, bool isLoading, bool canRetry)
        {
            bool visible = !string.IsNullOrEmpty(title);
            stateArea.SetActive(visible);
            listArea.SetActive(!visible);
            if (featuredArea) featuredArea.SetActive(!visible);
            stateTitle.text = title ?? "";
            stateDescription.text = description ?? "";
            retry.gameObject.SetActive(canRetry);
            loading = isLoading;
            loadingIcon.gameObject.SetActive(isLoading);
        }

        void Update()
        {
            if (loading) loadingIcon.Rotate(0, 0, -140 * Time.unscaledDeltaTime);
        }
    }
}
