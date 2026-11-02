using UnityEngine;
using TMPro;
using Salasasa.RightEntrances;

namespace Salasasa.Home
{
    public sealed class EntranceMainBridge : MonoBehaviour
    {
        public const string PalettePreference = "Salasasa.RightEntrances.Palette.v1";
        public const int DefaultPalette = 1;
        // Saved palette IDs stay unchanged when the picker display order changes.
        public static readonly int[] DisplayPaletteOrder = { 1, 0, 2, 3, 4, 5, 6, 7, 8, 9 };
        public CompleteLobbyController lobby;
        public EntranceRouter entrances;
        public NowPlayer serverStats;
        public MeunPanel menu;
        public GameStateNetworkManager games;
        public NetworkManager network;
        public EntrancePalette[] palettes;
        public EntranceTint[] tints;
        public GameObject palettePicker;
        public UnityEngine.UI.Button[] paletteButtons;
        public GameObject[] paletteMarks;
        public TMP_Text paletteLabel;
        public int defaultPalette = DefaultPalette;
        public int SelectedPalette { get; private set; }
        float nextRefresh, nextSpectatorRequest;
        bool wasVisible;

        void Awake()
        {
            SelectPalette(PlayerPrefs.GetInt(PalettePreference, DefaultPalette), false);
            entrances.SetCounts(-1, -1, -1);
            palettePicker.SetActive(false);
        }
        void OnEnable()
        {
            entrances.routeRequested.AddListener(Open);
            serverStats.ServerStatsChanged += RefreshCounts;
            menu.MatchPlayerCountChanged += RefreshCounts;
            games.SpectatorCountChanged += RefreshCounts;
            network.ConnectionAvailabilityChanged += ConnectionChanged;
        }
        void OnDisable()
        {
            entrances.routeRequested.RemoveListener(Open);
            serverStats.ServerStatsChanged -= RefreshCounts;
            menu.MatchPlayerCountChanged -= RefreshCounts;
            games.SpectatorCountChanged -= RefreshCounts;
            network.ConnectionAvailabilityChanged -= ConnectionChanged;
        }
        void ConnectionChanged(bool connected)
        {
            if (!connected) games.InvalidateSpectatorCount();
            nextSpectatorRequest = 0;
            RefreshCounts();
        }
        void Open(string route) { lobby.Open(route); }
        void Update()
        {
            bool visible = entrances.gameObject.activeInHierarchy;
            if (visible && !wasVisible) { entrances.Clear(); nextSpectatorRequest = 0; }
            wasVisible = visible;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f;
            RefreshCounts();
            if (visible && network.IsWebSocketOpen && UserDataManager.Instance != null && UserDataManager.Instance.UserId > 0
                && Time.unscaledTime >= nextSpectatorRequest)
            {
                nextSpectatorRequest = Time.unscaledTime + 10f;
                games.GetSpectatorList();
            }
        }
        public void RefreshCounts()
        {
            bool connected = network != null && network.IsWebSocketOpen;
            float now = Time.realtimeSinceStartup;
            int matching = connected && menu.HasMatchPlayerCount && menu.isActiveAndEnabled
                && now - menu.MatchPlayerCountReceivedAt <= 20f ? menu.MatchPlayerCount : -1;
            int rooms = connected && serverStats.HasServerStats && serverStats.isActiveAndEnabled
                && now - serverStats.ServerStatsReceivedAt <= 20f
                ? (int)System.Math.Min(int.MaxValue, (long)serverStats.WaitingRoomCount + serverStats.PlayingRoomCount) : -1;
            int spectatable = connected && now - games.SpectatorCountReceivedAt <= 20f ? games.SpectatorCount : -1;
            int venueWaiting = connected && serverStats.HasServerStats && serverStats.isActiveAndEnabled
                && now - serverStats.ServerStatsReceivedAt <= 20f ? serverStats.VenueWaitingRoomCount : -1;
            if (entrances.MatchingCount != matching) entrances.SetMatchingCount(matching);
            if (entrances.RoomCount != rooms) entrances.SetRoomCount(rooms);
            if (entrances.SpectatableCount != spectatable) entrances.SetSpectatableCount(spectatable);
            if (entrances.VenueWaitingCount != venueWaiting) entrances.SetVenueWaitingCount(venueWaiting);
        }
        public void TogglePalettePicker() { palettePicker.SetActive(!palettePicker.activeSelf); }
        public void ClosePalettePicker() { palettePicker.SetActive(false); }
        public void SelectPalette(int index) { SelectPalette(index, true); }
        public void SelectPalette(int index, bool remember)
        {
            SelectedPalette = Mathf.Clamp(index, 0, palettes.Length - 1);
            var palette = palettes[SelectedPalette];
            foreach (var tint in tints) if (tint) tint.Apply(palette);
            foreach (var entry in entrances.buttons) if (entry) entry.ApplyPalette(palette);
            for (int i = 0; i < paletteMarks.Length; i++) paletteMarks[i].SetActive(i == SelectedPalette);
            paletteLabel.text = palettes[SelectedPalette].name;
            if (remember) { PlayerPrefs.SetInt(PalettePreference, SelectedPalette); PlayerPrefs.Save(); }
        }
    }
}
