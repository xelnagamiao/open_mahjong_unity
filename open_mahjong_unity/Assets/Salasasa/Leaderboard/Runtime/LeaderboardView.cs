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
            [NonSerialized] public int listIndex = -1;
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
        LeaderboardEntry[] listEntries;
        LeaderboardPortrait[] listPortraits;
        bool listGrade, listSample;
        int listUser, firstVisible = -1;
        float rowHeight, rowSpacing;
        public event Action<EntryRow> RowBound;

        public void Bind(LeaderboardController owner)
        {
            ConfigureColumns();
            close.onClick.AddListener(owner.Close);
            retry.onClick.AddListener(owner.Refresh);
            if (myPlayer) myPlayer.onClick.AddListener(owner.ShowOwnPlayer);
            foreach (var row in rows) BindRow(row, owner);
            foreach (var row in featured) BindRow(row, owner);
            if (rows.Length > 0)
            {
                rowHeight = ((RectTransform)rows[0].root.transform).rect.height;
                var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
                if (layout) { rowSpacing = layout.spacing; layout.enabled = false; }
                var fitter = scroll.content.GetComponent<ContentSizeFitter>();
                if (fitter) fitter.enabled = false;
                scroll.onValueChanged.AddListener(OnListScrolled);
            }
        }

        // Keep the list and the personal row on the same columns, including older baked scenes.
        public void ConfigureColumns()
        {
            foreach (var row in rows)
                ConfigureColumns(row.position, row.username, row.uid, row.detail, row.score);
            ConfigureColumns(myPosition, myName, myHint, myGrade, myScore);
        }

        static void ConfigureColumns(TMP_Text position, TMP_Text name, TMP_Text uid, TMP_Text detail, TMP_Text score)
        {
            SetColumn(position, 6, 64);
            SetColumn(name, 132, 192);
            SetColumn(uid, 132, 192);
            SetColumn(detail, 332, 80);
            SetColumn(score, 420, 224);
            FitValue(position, 18, 26);
            FitValue(detail, 14, 21);
            FitValue(score, 14, 21);
        }

        static void SetColumn(TMP_Text text, float x, float width)
        {
            var rect = text.rectTransform;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }

        static void FitValue(TMP_Text text, float minSize, float maxSize)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
            var locale = text.GetComponent<LocalizedPlatformLabel>();
            if (locale)
            {
                locale.authoredAutoSize = true;
                locale.authoredSize = maxSize;
                locale.minimumSize = minSize;
            }
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

        public void SetRows(LeaderboardEntry[] entries, int rule, LeaderboardPortrait[] portraits, bool sample, int myId, string sourceText, int ownPosition = 0)
        {
            SetState(null, null, false, false);
            bool grade = LeaderboardRules.IsGrade(rule);
            for (int i = 0; i < featured.Length; i++) featured[i].Set(i < entries.Length ? entries[i] : null, grade, portraits, sample, myId);
            listEntries = entries;
            listPortraits = portraits;
            listGrade = grade;
            listSample = sample;
            listUser = myId;
            var count = Mathf.Max(0, entries.Length - firstListIndex);
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(0, count * (rowHeight + rowSpacing) - rowSpacing));
            source.text = "";
            listCount.text = "";
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
            UpdateVisibleRows(true);
            SetMine(entries, grade, portraits, sample, myId, ownPosition: ownPosition);
        }

        void OnListScrolled(Vector2 _) => UpdateVisibleRows(false);

        void UpdateVisibleRows(bool force)
        {
            if (listEntries == null || rows.Length == 0 || rowHeight <= 0) return;
            int count = Mathf.Max(0, listEntries.Length - firstListIndex);
            float stride = rowHeight + rowSpacing;
            int start = Mathf.Clamp(Mathf.FloorToInt(scroll.content.anchoredPosition.y / stride) - 1,
                0, Mathf.Max(0, count - rows.Length));
            if (!force && firstVisible == start) return;
            firstVisible = start;
            for (int offset = 0; offset < rows.Length; offset++)
            {
                int index = start + offset;
                var row = rows[index % rows.Length];
                if (!force && row.listIndex == index) continue;
                row.listIndex = index;
                var rect = (RectTransform)row.root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(0, -index * stride);
                row.Set(index < count ? listEntries[firstListIndex + index] : null, listGrade, listPortraits, listSample, listUser);
                if (row.entry != null) RowBound?.Invoke(row);
            }
        }

        public void SetMine(LeaderboardEntry[] entries, bool grade, LeaderboardPortrait[] portraits, bool sample, int myId, bool rankingResolved = true, int ownPosition = 0)
        {
            LeaderboardEntry mine = Array.Find(entries, x => x.user_id == myId);
            ApplyDefaultPortrait(portraits, myAvatar);
            myNameSource = mine != null ? mine.username : sample ? "SALASASA" : myId > 0 ? "我的排名" : "未登录";
            myNameIsPlaceholder = mine == null && !sample;
            myName.text = myNameIsPlaceholder ? PlatformText.Get(myNameSource) : myNameSource;
            int position = mine?.rank_position ?? ownPosition;
            myPosition.text = position > 0 ? position.ToString("00") : myId > 0 && rankingResolved ? "1000+" : "—";
            myGrade.text = mine == null ? "—" : LeaderboardRules.Detail(mine, grade);
            myScore.text = mine == null ? "—" : LeaderboardRules.Score(mine, grade) + (grade ? " PT" : " R");
            myHint.text = myId > 0 ? "UID " + myId : "登录后显示我的排名";
        }

        public void SetOwnRating(int userId, string username, RuleRating rating)
        {
            myNameSource = username;
            myNameIsPlaceholder = false;
            myName.text = username;
            myHint.text = "UID " + userId;
            var current = new LeaderboardEntry {
                rank_name = rating.rank_name, rank_score = rating.rank_score,
                elo = rating.elo, games = rating.games
            };
            bool grade = RankedRules.IsGrade(rating.rule);
            myGrade.text = LeaderboardRules.Detail(current, grade);
            myScore.text = LeaderboardRules.Score(current, grade) + (grade ? " PT" : " R");
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
