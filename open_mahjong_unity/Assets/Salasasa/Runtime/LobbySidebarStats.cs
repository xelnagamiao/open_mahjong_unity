using System.Globalization;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    public sealed class LobbySidebarStats : MonoBehaviour
    {
        public NowPlayer source;
        public NetworkManager network;
        public TMP_Text onlineValue, roomValue, waitingValue, playingValue, matchValue;
        public UnityEngine.UI.Graphic freshnessMark;
        public Color freshColor = new Color(.345f, .81f, 1f, 1f);
        public Color unavailableColor = new Color(.51f, .56f, .62f, 1f);
        bool showingFresh;
        float nextFreshnessCheck;

        public bool IsFresh => source != null && source.HasServerStats && source.isActiveAndEnabled
            && network != null && network.IsWebSocketOpen
            && Time.realtimeSinceStartup - source.ServerStatsReceivedAt <= 20f;

        void OnEnable()
        {
            if (source != null) source.ServerStatsChanged += Refresh;
            ConfigManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (source != null) source.ServerStatsChanged -= Refresh;
            ConfigManager.OnLanguageChanged -= Refresh;
        }

        void Update()
        {
            if (Time.unscaledTime < nextFreshnessCheck) return;
            nextFreshnessCheck = Time.unscaledTime + .5f;
            if (showingFresh != IsFresh) Refresh();
        }

        public void Refresh()
        {
            showingFresh = IsFresh;
            Set(onlineValue, showingFresh ? Number(source.OnlinePlayerCount) : "—");
            Set(roomValue, showingFresh ? Number((long)source.WaitingRoomCount + source.PlayingRoomCount) : "—");
            Set(waitingValue, PlatformText.Format("等待中 {0}", showingFresh ? Number(source.WaitingRoomCount) : "—"));
            Set(playingValue, PlatformText.Format("游戏中 {0}", showingFresh ? Number(source.PlayingRoomCount) : "—"));
            Set(matchValue, PlatformText.Format("游戏中匹配数: {0}", showingFresh ? Number(source.MatchPlayingGames) : "—"));
            if (freshnessMark != null) freshnessMark.color = showingFresh ? freshColor : unavailableColor;
        }

        static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static void Set(TMP_Text target, string value)
        {
            if (target != null && target.text != value) target.text = value;
        }
    }
}
