using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Salasasa.Leaderboards
{
    public sealed class LeaderboardController : MonoBehaviour
    {
        public enum DataSource { Sample, LocalWeb, AuthenticatedServer }
        public GameObject popup;
        public LeaderboardView[] views;
        public LeaderboardPaletteEditor paletteEditor;
        public Button openButton;
        public Button[] sourceButtons, stateButtons;
        public TMP_Text sourceSwitchLabel;
        public LeaderboardPortrait[] portraits;
        public DataSource dataSource = DataSource.LocalWeb;
        public string publicWebUrl => ConfigManager.webApiUrl.TrimEnd('/') + "/api/player/leaderboard?limit=20";
        public int selectedRule, authenticatedUserId;
        public bool openOnStart = true;
        public bool externalKeyboard;
        public const string StylePreference = "SalasasaInterface.LeaderboardStyle.SideRailV2";
        public event Action DataApplied;
        public bool IsOpen => popup != null && popup.activeInHierarchy && !(ModalFade && ModalFade.IsClosing);
        Salasasa.Home.BakedModalFade modalFade;
        Salasasa.Home.BakedModalFade ModalFade => modalFade ? modalFade : modalFade = popup ? popup.GetComponent<Salasasa.Home.BakedModalFade>() : null;
        public event Action<string, string> ServerRequest;
        public string ActiveRequestId { get; private set; }
        public int VisibleEntryCount => entries.Length;
        public int RankingUserId => MyId;
        public string CurrentState { get; private set; }
        public string LastSourceText { get; private set; }
        LeaderboardEntry[] entries = Array.Empty<LeaderboardEntry>();
        Coroutine request;
        int generation;
        bool bound;

        void Awake()
        {
            Bind();
        }

        void Bind()
        {
            if (bound) return;
            bound = true;
            foreach (var view in views) view.Bind(this);
            if (openButton != null) openButton.onClick.AddListener(Open);
            for (int i = 0; i < sourceButtons.Length; i++) { int n = i; if (sourceButtons[i]) sourceButtons[i].onClick.AddListener(() => SelectSource(n)); }
            for (int i = 0; i < stateButtons.Length; i++) { int n = i; if (stateButtons[i]) stateButtons[i].onClick.AddListener(() => PreviewState(n)); }
        }

        void Start()
        {
            if (paletteEditor) paletteEditor.Initialize();
            if (openOnStart) Open(); else Close();
        }

        void Update()
        {
            if (externalKeyboard || !IsOpen) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            bool escape = keyboard.escapeKey.wasPressedThisFrame;
#else
            bool escape = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (escape) HandleEscape();
        }

        public void Open()
        {
            Bind();
            if (ModalFade) ModalFade.Show(); else popup.SetActive(true);
            if (paletteEditor) paletteEditor.Initialize();
            Refresh();
        }
        public void Close()
        {
            CancelRequest();
            ActiveRequestId = null;
            if (ModalFade) ModalFade.Hide(); else popup.SetActive(false);
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
        }
        public bool HandleEscape()
        {
            if (!IsOpen) return false;
            if (ProfileOnClick.PendingUserId >= 10 || (PlayerInfoPanel.Instance && PlayerInfoPanel.Instance.IsOpen)) return false;
            Close();
            return true;
        }

        public void SelectRule(int index)
        {
            if (index < 0 || index >= LeaderboardRules.Ids.Length || selectedRule == index) return;
            selectedRule = index;
            Refresh();
        }

        public void SelectSource(int index)
        {
            dataSource = (DataSource)Mathf.Clamp(index, 0, 2);
            if (dataSource == DataSource.LocalWeb) selectedRule = 0;
            sourceSwitchLabel.text = dataSource == DataSource.Sample ? "示例数据" : dataSource == DataSource.LocalWeb ? "本地实时榜单" : "游戏服务器";
            Refresh();
        }

        public void Refresh()
        {
            CancelRequest();
            ActiveRequestId = Guid.NewGuid().ToString("N");
            entries = Array.Empty<LeaderboardEntry>();
            foreach (var view in views)
            {
                view.SetRule(selectedRule);
                view.source.text = dataSource == DataSource.Sample ? "示例数据" : "";
                view.listCount.text = "";
                view.SetMine(entries, LeaderboardRules.IsGrade(selectedRule), portraits, dataSource == DataSource.Sample, MyId, false);
            }
            ShowState("loading", "正在获取排行榜", "", true, false);
            request = StartCoroutine(Load(generation));
        }

        int MyId => dataSource == DataSource.Sample ? LeaderboardSamples.MyUserId : authenticatedUserId;

        void CancelRequest()
        {
            generation++;
            if (request != null) StopCoroutine(request);
            request = null;
        }

        IEnumerator Load(int version)
        {
            if (dataSource == DataSource.Sample)
            {
                yield return new WaitForSecondsRealtime(.18f);
                Apply(LeaderboardSamples.Create(selectedRule), "示例数据 · 前 100 名预览");
                yield break;
            }
            if (dataSource == DataSource.AuthenticatedServer)
            {
                if (ServerRequest == null) { ShowState("error", "尚未连接游戏服务器", "登录后可查看各规则的实时排行", false, true); yield break; }
                ServerRequest.Invoke(LeaderboardRules.Ids[selectedRule], ActiveRequestId);
                float end = Time.realtimeSinceStartup + 15;
                while (version == generation && CurrentState == "loading" && Time.realtimeSinceStartup < end) yield return null;
                if (version == generation && CurrentState == "loading") ShowState("error", "获取排行榜超时", "请检查网络连接后重试", false, true);
                yield break;
            }
            if (selectedRule != 0)
            {
                ShowState("unavailable", "请登录游戏后查看", "当前公开榜单仅提供国标排行", false, true);
                yield break;
            }
            using (var web = UnityWebRequest.Get(publicWebUrl))
            {
                web.timeout = 15;
                yield return web.SendWebRequest();
                if (version != generation) yield break;
                if (web.result != UnityWebRequest.Result.Success) { ShowState("error", "暂时无法获取榜单", "请检查网络连接后重试", false, true); yield break; }
                LeaderboardWebReply reply;
                try { reply = JsonUtility.FromJson<LeaderboardWebReply>(web.downloadHandler.text); }
                catch (Exception) { reply = null; }
                if (reply == null || !reply.success || reply.data == null) { ShowState("error", "榜单暂不可用", "请稍后重新加载", false, true); yield break; }
                Apply(reply.data, "实时国标 · 前 20 名 · " + DateTime.Now.ToString("HH:mm") + " 更新");
            }
        }

        public void ApplyServerResponse(string json)
        {
            LeaderboardServerReply reply;
            try { reply = JsonUtility.FromJson<LeaderboardServerReply>(json); }
            catch (Exception) { return; }
            ApplyServerResponse(reply);
        }

        public void ApplyServerResponse(LeaderboardServerReply reply)
        {
            if (dataSource != DataSource.AuthenticatedServer || !IsOpen || string.IsNullOrEmpty(ActiveRequestId)) return;
            if (reply == null || reply.data_request_id != ActiveRequestId || reply.rating_rule != LeaderboardRules.Ids[selectedRule]) return;
            if (!reply.success || reply.leaderboard_list == null) { ShowState("error", "暂时无法获取榜单", "请稍后重新加载", false, true); return; }
            Apply(reply.leaderboard_list, "实时榜单 · 前 100 名 · " + DateTime.Now.ToString("HH:mm") + " 更新");
        }

        void Apply(LeaderboardEntry[] data, string source)
        {
            entries = data ?? Array.Empty<LeaderboardEntry>();
            LastSourceText = source;
            foreach (var view in views) view.SetRows(entries, selectedRule, portraits, dataSource == DataSource.Sample, MyId, source);
            CurrentState = entries.Length > 0 ? "ready" : "empty";
            DataApplied?.Invoke();
            if (entries.Length == 0) ShowState("empty", "暂无排行数据", "当前规则还没有上榜玩家", false, true);
        }

        void ShowState(string state, string title, string description, bool loading, bool retry)
        {
            CurrentState = state;
            foreach (var view in views) view.SetState(title, description, loading, retry);
        }

        public void PreviewState(int index)
        {
            CancelRequest();
            dataSource = DataSource.Sample;
            sourceSwitchLabel.text = "示例数据";
            entries = Array.Empty<LeaderboardEntry>();
            foreach (var view in views)
            {
                view.source.text = "示例状态";
                view.listCount.text = "";
                view.SetMine(entries, LeaderboardRules.IsGrade(selectedRule), portraits, true, MyId, index == 2);
            }
            if (index == 0) Refresh();
            if (index == 1) ShowState("loading", "正在获取排行榜", "", true, false);
            if (index == 2) ShowState("empty", "暂无排行数据", "当前规则还没有上榜玩家", false, true);
            if (index == 3) ShowState("error", "暂时无法获取榜单", "请检查网络连接后重试", false, true);
        }

        public void ShowPlayer(LeaderboardEntry entry)
        {
            if (entry == null) return;
            ProfileOnClick.OpenPlayerInfo(entry.user_id, LeaderboardRules.Ids[selectedRule]);
        }
        public void ShowOwnPlayer()
        {
            if (RankingUserId >= 10) ProfileOnClick.OpenPlayerInfo(RankingUserId, LeaderboardRules.Ids[selectedRule]);
        }

        void OnDisable() { CancelRequest(); }
    }
}
