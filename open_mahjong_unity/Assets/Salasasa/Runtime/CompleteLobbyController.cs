using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Salasasa.Home
{
    public sealed class CompleteLobbyController : MonoBehaviour
    {
        public LobbyShowcase artwork;
        public Salasasa.Leaderboards.LeaderboardLobbyBridge leaderboard;
        public Salasasa.Statistics.StatisticsLobbyBridge statistics;
        public GameObject sourceMenu, bodyLayer;
        public GameObject lobbyOverlays;
        public CanvasGroup roomList;
        public Canvas boardCanvas;
        public Canvas gameCanvas;
        public RectTransform pageRoot;
        public Canvas productionCanvas;
        public GameObject versionLabel;
        public GameObject classroom;
        public DeveloperOptionsPanel developerOptions;
        public InventoryPanel inventory;
        public ShopPanel shop;
        public TMP_Text[] matchCounts;
        public MailPanel mail;
        public FourSideHeader headerNavigation;
        public bool RoomListOpen { get; private set; }
        public bool ChatOpen { get; private set; }
        public string SelectedRoute { get; private set; } = "home";
        bool readyForLogin, guestStarted;
        string observedWindow;
        InterfacePlansController interfacePlans;
        UnityEngine.UI.GraphicRaycaster[] gameRaycasters;

        void Awake()
        {
            artwork.actionHandler = Route;
            interfacePlans=artwork.previewControls.GetComponent<InterfacePlansController>();
            gameRaycasters=gameCanvas?gameCanvas.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true):Array.Empty<UnityEngine.UI.GraphicRaycaster>();

        }
        void Start()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectionAvailabilityChanged += ConnectionChanged;
            artwork.SelectTheme(0,false);
        }
        void ConnectionChanged(bool ready) { readyForLogin=ready; }
        void Update()
        {
            if (readyForLogin && !guestStarted && CompleteLobbyEnvironment.Has("-auto-guest") && !CompleteLobbyEnvironment.Has("-manual-login"))
            { guestStarted=true; StartCoroutine(GuestLogin()); }
            var key=Keyboard.current;
            if(key==null) return;
            if(key.escapeKey.wasPressedThisFrame)
            {
                if(interfacePlans!=null && interfacePlans.HandleEscape())return;
                if(mail && mail.IsOpen) {mail.Close();SelectedRoute=CurrentPageRoute();return;}
                if(leaderboard!=null && leaderboard.HandleEscape())return;
                if(shop && shop.IsOpen) {shop.Close();SelectedRoute=CurrentPageRoute();return;}
                if(inventory && inventory.IsOpen) {inventory.Close();SelectedRoute=CurrentPageRoute();return;}
                if(ProfileOnClick.PendingUserId>=10 || (PlayerInfoPanel.Instance && PlayerInfoPanel.Instance.IsOpen)) {ClosePlayerData();SelectedRoute=CurrentPageRoute();return;}
                if(headerNavigation!=null && headerNavigation.HandleEscape())return;
                if(statistics!=null && statistics.HandleEscape())return;
                if(developerOptions && developerOptions.IsOpen) {developerOptions.Close();return;}
                if((classroom!=null && classroom.activeSelf) || (MeunPanel.Instance && MeunPanel.Instance.IsFanTableOpen)) CloseTutorial();
                else if(ChatOpen) CloseChat();
                else if(RoomListOpen) OpenHome();
            }
        }
        IEnumerator GuestLogin()
        {
            yield return new WaitForSecondsRealtime(1);
            if(WindowsManager.Instance != null && WindowsManager.Instance.GetCurrentWindow()=="login") NetworkManager.Instance.TouristLogin();
        }
        void LateUpdate()
        {
            var windows=WindowsManager.Instance;
            if(windows==null) return;
            string current=windows.GetCurrentWindow();
            bool game=current=="game" || current=="recordscene";
            if(gameCanvas)gameCanvas.enabled=game;
            foreach(var raycaster in gameRaycasters)if(raycaster)raycaster.enabled=game;
            if(boardCanvas!=null)boardCanvas.enabled=game||current=="sceneConfig";
            if(versionLabel!=null)versionLabel.SetActive(true);
            bool logged=current!="login" && !game;
            if(lobbyOverlays)lobbyOverlays.SetActive(logged);
            if(artwork.gameObject.activeSelf==game) artwork.gameObject.SetActive(!game);
            if(current!=observedWindow)
            {
                bool justLoggedIn = observedWindow == "login" && current == "menu";
                observedWindow=current;
                if (justLoggedIn) RoomListOpen=true;
                ClosePlayerData();
                if(inventory)inventory.Close();
                if(shop)shop.Close();
                if(current!="menu") {RoomListOpen=false; CloseChat();}
                if(!logged) {headerNavigation.CloseUtility();CloseTutorial();if(mail)mail.Close();}
                SelectedRoute=mail && mail.IsOpen?"mail":current=="menu" && statistics!=null && statistics.IsOpen?"statistics":current=="menu" && RoomListOpen?"room":ToRoute(current);
            }
            bool home=current=="menu" && !RoomListOpen && (statistics==null || !statistics.IsOpen) && (!mail || !mail.IsOpen);
            if(SelectedRoute=="mail" && mail && !mail.IsOpen) SelectedRoute=CurrentPageRoute();
            if((SelectedRoute=="shop" && (!shop || !shop.IsOpen)) ||
                (SelectedRoute=="inventory" && (!inventory || !inventory.IsOpen)) ||
                (SelectedRoute=="chat" && !ChatOpen) ||
                (SelectedRoute=="tutorial" && (!MeunPanel.Instance || !MeunPanel.Instance.IsFanTableOpen)) ||
                (SelectedRoute=="profile" && ProfileOnClick.PendingUserId<10 && (!PlayerInfoPanel.Instance || !PlayerInfoPanel.Instance.IsOpen)))
                SelectedRoute=CurrentPageRoute();
            bodyLayer.SetActive(home && !artwork.overlay.IsOpen);
            SetVisible(roomList,current=="menu" && RoomListOpen);
            if(MeunPanel.Instance)
            {
                string value=PlatformText.Format("匹配人数({0})",MeunPanel.Instance.MatchPlayerCount);
                foreach(var count in matchCounts)if(count && count.text!=value)count.text=value;
            }
            bool showArt=current!="sceneConfig";
            artwork.background.gameObject.SetActive(showArt);artwork.backgroundImage.gameObject.SetActive(showArt);
            artwork.characterLayer.SetActive(showArt && artwork.showCharacterFill);
            if(artwork.artworkShade!=null)artwork.artworkShade.gameObject.SetActive(showArt);
            if(headerNavigation!=null)headerNavigation.Tick(logged,current,home);
        }
        static void SetVisible(CanvasGroup group,bool visible)
        {
            if(group==null)return;
            group.alpha=visible?1:0;group.interactable=group.blocksRaycasts=visible;
        }
        public void Route(LobbyAction action) => Open(action.action);
        public void NavigateFromHeader(string route)
        {
            string destination=route;
            bool page=destination=="room" || destination=="ranked" || destination=="history" ||
                destination=="spectate" || destination=="event" || destination=="friends" ||
                destination=="notice" || destination=="settings" || destination=="statistics" ||
                destination=="about" || destination=="sceneConfig" || destination=="community";
            if(page && CurrentPageRoute()==destination) OpenHome();
            else Open(route);
        }
        public string CurrentPageRoute()
        {
            var windows=WindowsManager.Instance;
            if(windows==null)return "home";
            string current=windows.GetCurrentWindow();
            if(current=="menu")
            {
                if(statistics!=null && statistics.IsOpen)return "statistics";
                if(RoomListOpen)return "room";
            }
            return ToRoute(current);
        }
        public void Open(string route)
        {
            headerNavigation.CloseUtility();
            if(route=="mail" && mail && mail.IsOpen) {mail.Close();SelectedRoute=CurrentPageRoute();return;}
            if(route=="chat" && ChatOpen) {CloseChat();SelectedRoute=CurrentPageRoute();return;}
            if(route=="shop" && shop && shop.IsOpen) {shop.Close();SelectedRoute=CurrentPageRoute();return;}
            if(route=="inventory" && inventory && inventory.IsOpen) {inventory.Close();SelectedRoute=CurrentPageRoute();return;}
            if(route=="profile" && ((ProfileOnClick.PendingUserId>=10 && ProfileOnClick.PendingUserId==UserDataManager.Instance.UserId) ||
                (PlayerInfoPanel.Instance && PlayerInfoPanel.Instance.IsOpen && PlayerInfoPanel.Instance.CurrentUserId==UserDataManager.Instance.UserId)))
            {ClosePlayerData();SelectedRoute=CurrentPageRoute();return;}
            if((route=="tutorial" || route=="help" || route=="rules") &&
                (MeunPanel.Instance && MeunPanel.Instance.IsFanTableOpen))
            {CloseTutorial();SelectedRoute=CurrentPageRoute();return;}
            if((route=="developer" || route=="classroom") && !CompleteLobbyEnvironment.IsDevelopment)return;
            if(developerOptions)developerOptions.Close();
            if(route=="silhouette") {HomeUiVisibility.Toggle(this);return;}
            if(route=="controls") {artwork.previewControls.SetActive(!artwork.previewControls.activeSelf);return;}
            if(route=="character" || route=="appearance") {if(CompleteLobbyEnvironment.IsDevelopment)artwork.overlay.Open("character","场景配置");return;}
            if(route!="profile")ClosePlayerData();
            if(statistics!=null && route!="statistics")statistics.Close();
            if(leaderboard!=null && route!="leaderboard")leaderboard.Close();
            if(inventory && route!="inventory")inventory.Close();
            if(shop && route!="shop")shop.Close();
            if(mail && route!="mail")mail.Close();
            artwork.overlay.Close();
            CloseTutorial();
            if(route!="chat") CloseChat();
            if(route=="leaderboard" && leaderboard!=null) {leaderboard.Open();return;}
            if(route=="statistics" && statistics!=null) {OpenHome();statistics.Open();SelectedRoute="statistics";return;}
            if(route=="home") {OpenHome();return;}
            if(route=="community") {WindowsManager.Instance.SwitchWindow("community");SelectedRoute="community";return;}
            if(route=="ranked" && WindowsManager.Instance.GetCurrentWindow()=="match") {OpenHome();return;}
            if(route=="room" && UserDataManager.Instance.RoomId==UserDataManager.ROOM_ID_NONE)
            {
                WindowsManager.Instance.SwitchWindow("menu");RoomListOpen=true;SelectedRoute="room";return;
            }
            if(route=="inventory") {inventory.Open();SelectedRoute="inventory";return;}
            if(route=="shop") {if(shop){shop.Open();SelectedRoute="shop";}return;}
            if(route=="developer") {developerOptions.Show();return;}
            if(route=="classroom") {if(classroom){OpenHome();classroom.SetActive(true);SelectedRoute="classroom";}return;}
            if(route=="tutorial" || route=="help" || route=="rules") {OpenRules();return;}
            if(route=="chat") {OpenChat();return;}
            if(route=="profile") {ProfileOnClick.OpenPlayerInfo(UserDataManager.Instance.UserId,UserContainer.Instance.CurrentRule);SelectedRoute="profile";return;}
            if(route=="mail")
            {
                if(mail) {OpenHome();mail.Open();SelectedRoute="mail";}
                return;
            }
            var windows=WindowsManager.Instance;
            switch(route)
            {
                case "room":
                    if(!LobbyStateGuard.IsInRoom && LobbyStateGuard.BlockIfInMatchQueueForRoom())return;
                    windows.SwitchWindow("room");break;
                case "history":
                    windows.SwitchWindow("record");RecordPanel.Instance.OpenAndReload();break;
                case "settings": windows.SwitchWindow("config");break;
                case "about": windows.SwitchWindow("aboutUs");break;
                case "notice": windows.SwitchWindow("notice");break;
                case "sceneConfig": windows.SwitchWindow("sceneConfig");break;
                case "spectate": windows.SwitchWindow("spectator");break;
                case "ranked": windows.SwitchWindow("match");break;
                case "event": windows.SwitchWindow("event");break;
                case "friends":
                    windows.SwitchWindow("friend");FriendNetworkManager.Instance.ListAllFriendPanels();break;
                case "game": windows.ResumeSuspendedGame();break;
                case "logout":
                    NotificationManager.Instance.ShowMessage("登出","确定要登出当前账号吗？","logout_confirm");break;
                default: Debug.LogError("Unknown lobby route: " + route);return;
            }
            SelectedRoute=route;
        }
        public void OpenHome()
        {
            ClosePlayerData();
            if(inventory)inventory.Close();
            if(shop)shop.Close();
            if(mail)mail.Close();
            if(statistics!=null)statistics.Close();
            if(leaderboard!=null)leaderboard.Close();
            RoomListOpen=false;CloseChat();CloseTutorial();artwork.overlay.Close();
            if(developerOptions)developerOptions.Close();
            WindowsManager.Instance.SwitchWindow("menu");SelectedRoute="home";
        }
        void ClosePlayerData()
        {
            ProfileOnClick.CancelPlayerInfo();
            if(PlayerInfoPanel.Instance && PlayerInfoPanel.Instance.IsOpen)PlayerInfoPanel.Instance.Close();
        }
        public void CloseTutorial()
        {
            if(classroom!=null)classroom.SetActive(false);
            if(MeunPanel.Instance)MeunPanel.Instance.CloseFanTable();
        }
        void OpenRules()
        {
            OpenHome();
            MeunPanel.Instance.OpenFanTable();SelectedRoute="tutorial";
        }
        void OpenChat()
        {
            OpenHome();
            ChatOpen=true;
            SelectedRoute="chat";
        }
        public void ToggleTableChat()
        {
            string window=WindowsManager.Instance?.GetCurrentWindow();
            if(window=="game"||window=="recordscene")ChatOpen=!ChatOpen;
        }
        public void CloseChat() => ChatOpen=false;
        static string ToRoute(string window) => window switch
        {
            "menu"=>"home", "record"=>"history", "config"=>"settings", "aboutUs"=>"about", "sceneConfig"=>"sceneConfig", "spectator"=>"spectate", "match"=>"ranked", "friend"=>"friends", _=>window
        };
        void OnDestroy()
        {
            if(NetworkManager.Instance!=null)NetworkManager.Instance.ConnectionAvailabilityChanged-=ConnectionChanged;
            if(artwork!=null)artwork.actionHandler=null;
        }
    }
}
