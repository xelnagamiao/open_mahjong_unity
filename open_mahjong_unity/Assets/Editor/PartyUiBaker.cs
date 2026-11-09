#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Incrementally authors the party dock and shared invitations in the real lobby scene.</summary>
public static class PartyUiBaker {
    static TMP_FontAsset font;
    static readonly Color Surface = new Color(.12f, .14f, .18f), Field = new Color(.18f, .21f, .27f);
    static readonly Color Ink = new Color(.93f, .94f, .96f), Muted = new Color(.63f, .68f, .75f), Accent = new Color(.76f, .64f, .42f);
    static T Get<T>(Transform t) where T : Component {
        var component = t.GetComponent<T>();
        return component ? component : t.gameObject.AddComponent<T>();
    }
    static RectTransform Rect(Transform parent, string name) {
        var existing = parent.Find(name) as RectTransform;
        if (existing) return existing;
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        Undo.RegisterCreatedObjectUndo(go, "Author party UI"); go.transform.SetParent(parent, false); return (RectTransform)go.transform;
    }
    static void Place(RectTransform r, float x, float y, float w, float h) {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
    }
    static void Fill(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static Image Box(Transform parent, string name, Color color) { var image = Get<Image>(Rect(parent, name)); image.color = color; image.raycastTarget = false; return image; }
    static TMP_Text Text(Transform parent, string name, string value, int size = 22) {
        var text = Get<TextMeshProUGUI>(Rect(parent, name)); text.font = font; text.text = value; text.fontSize = size; text.color = Ink;
        text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular; text.richText = false; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
        var bind = Get<PlatformFontBinding>(text.transform); bind.authoredFont = font; bind.authoredMaterial = font.material;
        bind.authoredStyle = FontStyles.Normal; bind.authoredWeight = FontWeight.Regular; bind.styleCaptured = true;
        return text;
    }
    static Button Button(Transform parent, string name, string value) {
        var image = Box(parent, name, Field); image.raycastTarget = true;
        var button = Get<Button>(image.transform); button.targetGraphic = image;
        var label = Text(button.transform, "Label", value, 20); Fill(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    static RectTransform Modal(Transform overlay, string name, string title, float width, float height, out Button close) {
        var root = Rect(overlay, name); Fill(root);
        UiCanvasLayers.Apply(Get<Canvas>(root), UiCanvasLayers.Modal, UiCanvasLayers.ModalOrder + 40); Get<GraphicRaycaster>(root); Get<BakedModalFade>(root);
        var shade = Box(root, "Shade", new Color(0, 0, 0, .55f)); Fill(shade.rectTransform); shade.raycastTarget = true;
        var fit = Rect(root, "Fit"); fit.anchorMin = fit.anchorMax = fit.pivot = Vector2.one * .5f; fit.sizeDelta = Vector2.zero;
        var window = Box(fit, "Window", Surface); var r = window.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = Vector2.one * .5f; r.sizeDelta = new Vector2(width, height); window.raycastTarget = true;
        var fitter = Get<ViewportPanelFit>(fit); fitter.panel = r; fitter.margin = 24;
        var line = Box(r, "Accent", Accent); Place(line.rectTransform, 0, 0, width, 2);
        var heading = Text(r, "Title", title, 30); Place(heading.rectTransform, 30, 15, width - 120, 52);
        close = Button(r, "Close", "×"); Place((RectTransform)close.transform, width - 64, 18, 42, 42);
        return r;
    }
    static void Set(Object target, string field, Object value) { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static Object Ref(Object target, string field) => new SerializedObject(target).FindProperty(field).objectReferenceValue;
    static void Array(Object target, string field, Object[] values) { var so = new SerializedObject(target); var p = so.FindProperty(field); p.arraySize = values.Length; for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    static Object[] ReadArray(Object target, string field) { var p = new SerializedObject(target).FindProperty(field); return Enumerable.Range(0, p.arraySize).Select(i => p.GetArrayElementAtIndex(i).objectReferenceValue).ToArray(); }

    [MenuItem("Tools/Mahjong/Main Scene/Bake Party and Friend Invitations")]
    public static void Bake() {
        if (Application.isPlaying) throw new InvalidOperationException("Bake outside Play Mode");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene first");
        var roots = scene.GetRootGameObjects();
        var network = roots.SelectMany(r => r.GetComponentsInChildren<NetworkManager>(true)).Single();
        var overlay = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "OverlayCanvas").transform;
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset");
        var manager = Get<PartyNetworkManager>(network.transform);
        manager.friendPicker = BakePicker(overlay); manager.incomingPanel = BakeIncoming(overlay);
        BakeDock(scene); BakeMatch(scene);
        var share = roots.SelectMany(r => r.GetComponentsInChildren<RoomInvitePanel>(true)).Single();
        BakeRoomInvitation(share, manager.friendPicker);
        foreach (var root in roots) foreach (var component in root.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Validate(scene);
    }
    [MenuItem("Tools/Mahjong/Main Scene/Update Match Party Panel")]
    public static void UpdateMatchPartyPanel() {
        if (Application.isPlaying) throw new InvalidOperationException("Author outside Play Mode");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene first");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset");
        BakeDock(scene);
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PartyNetworkManager>(true)).Single();
        manager.friendPicker.subtitle.text = "邀请好友组队";
        manager.incomingPanel.message.text = "好友邀请你组队";
        EditorUtility.SetDirty(manager.friendPicker.subtitle); EditorUtility.SetDirty(manager.incomingPanel.message);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    public static void BakeRoomInvitation(RoomInvitePanel share, FriendInvitePanel picker) {
        if (!share || !picker) return;
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset");
        var frame = share.primary.transform.parent;
        var invite = Button(frame, "InviteOnlineFriends", "邀请好友"); Place((RectTransform)invite.transform, 465, 276, 200, 42);
        Place((RectTransform)share.primary.transform, 235, 276, 200, 42);
        share.friendInvite = invite; share.friendPicker = picker;
    }
    public static void BuildPreview() {
        Debug.Log("[PartyPreview] Baking MainScene party UI");
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity"); Bake();
        BuildPlayer();
    }
    public static void BuildBakedPreview() {
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        Validate(SceneManager.GetActiveScene());
        BuildPlayer();
    }
    static void BuildPlayer() {
        string folder = Environment.GetEnvironmentVariable("OM_PARTY_PREVIEW_OUTPUT");
        if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("Preview output required");
        System.IO.Directory.CreateDirectory(folder);
        Debug.Log("[PartyPreview] Building Windows player: " + folder);
        var target = BuildTarget.StandaloneWindows64;
        bool useDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
        var graphics = PlayerSettings.GetGraphicsAPIs(target);
        bool localD3d11 = Environment.GetEnvironmentVariable("OM_PARTY_PREVIEW_D3D11") == "1";
        if (localD3d11) {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        }
        UnityEditor.Build.Reporting.BuildReport result;
        try { result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/MainScene.unity" }, locationPathName = System.IO.Path.Combine(folder, "MahjongParty.exe"),
            target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.Development
        }); } finally {
            if (localD3d11) { PlayerSettings.SetGraphicsAPIs(target, graphics); PlayerSettings.SetUseDefaultGraphicsAPIs(target, useDefault); }
        }
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Party preview build failed: " + result.summary.result);
        Debug.Log("[PartyPreview] Windows build succeeded");
    }

    static FriendInvitePanel BakePicker(Transform overlay) {
        var window = Modal(overlay, "FriendInvitePicker", "邀请好友", 1080, 700, out var close);
        var root = window.parent.parent; var p = Get<FriendInvitePanel>(root);
        p.fade = Get<BakedModalFade>(root); p.title = window.Find("Title").GetComponent<TMP_Text>(); p.close = close;
        p.subtitle = Text(window, "Subtitle", "邀请好友组队", 20); p.subtitle.color = Muted; Place(p.subtitle.rectTransform, 30, 72, 700, 32);
        var field = Box(window, "Search", Field); Place(field.rectTransform, 30, 120, 1020, 46); field.raycastTarget = true;
        p.search = Get<TMP_InputField>(field.transform); var area = Rect(field.transform, "Area"); Fill(area); area.offsetMin = new Vector2(16, 0); area.offsetMax = new Vector2(-16, 0);
        p.search.textViewport = area; p.search.textComponent = (TextMeshProUGUI)Text(area, "Text", "", 20); Fill(p.search.textComponent.rectTransform);
        var hint = Text(area, "Placeholder", "搜索好友昵称或 UID", 20); hint.color = Muted; Fill(hint.rectTransform); p.search.placeholder = hint; p.search.characterLimit = 64;
        Get<WebGLSupport.WebGLInput>(field.transform);
        var viewport = Rect(window, "Friends"); Place(viewport, 30, 188, 1020, 480); Get<RectMask2D>(viewport);
        var viewportImage = Get<Image>(viewport); viewportImage.color = Color.clear; viewportImage.raycastTarget = true;
        var scroll = Get<ScrollRect>(viewport); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        p.content = Rect(viewport, "Content"); p.content.anchorMin = new Vector2(0, 1); p.content.anchorMax = new Vector2(1, 1); p.content.pivot = new Vector2(.5f, 1); p.content.sizeDelta = Vector2.zero; scroll.content = p.content;
        var grid = Get<GridLayoutGroup>(p.content); grid.cellSize = new Vector2(498, 128); grid.spacing = new Vector2(20, 16); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
        Get<ContentSizeFitter>(p.content).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var card = Box(p.content, "FriendTemplate", Field); p.rowTemplate = Get<FriendInviteRow>(card.transform);
        var row = p.rowTemplate; row.avatar = Box(card.transform, "Avatar", Color.white); Place(row.avatar.rectTransform, 16, 20, 80, 80);
        row.username = Text(card.transform, "Username", "好友昵称", 23); Place(row.username.rectTransform, 114, 14, 360, 38);
        row.state = Text(card.transform, "State", "在线", 18); Place(row.state.rectTransform, 114, 65, 180, 32);
        row.action = Button(card.transform, "Invite", "邀请"); Place((RectTransform)row.action.transform, 332, 68, 144, 38); row.actionLabel = row.action.GetComponentInChildren<TMP_Text>();
        card.gameObject.SetActive(false);
        p.empty = Text(window, "Empty", "正在获取好友列表…", 23); p.empty.alignment = TextAlignmentOptions.Center; Place(p.empty.rectTransform, 30, 300, 1020, 70);
        p.ConfigureScrolling();
        root.gameObject.SetActive(false); return p;
    }
    static PartyIncomingPanel BakeIncoming(Transform overlay) {
        var window = Modal(overlay, "PartyIncomingInvite", "好友邀请", 620, 300, out var close);
        var root = window.parent.parent; var p = Get<PartyIncomingPanel>(root); p.fade = Get<BakedModalFade>(root); p.close = close;
        p.message = Text(window, "Message", "好友邀请你组队", 24); p.message.textWrappingMode = TextWrappingModes.Normal; Place(p.message.rectTransform, 30, 85, 560, 100);
        p.countdown = Text(window, "Countdown", "60s", 18); Place(p.countdown.rectTransform, 30, 232, 100, 42);
        p.decline = Button(window, "Decline", "拒绝"); Place((RectTransform)p.decline.transform, 224, 232, 160, 42);
        p.accept = Button(window, "Accept", "接受"); Place((RectTransform)p.accept.transform, 410, 232, 180, 42);
        root.gameObject.SetActive(false); return p;
    }
    static readonly Color DockSurface = new Color(.97f, .98f, .97f), DockField = new Color(.89f, .93f, .91f);
    static readonly Color DockInk = new Color(.22f, .29f, .25f);
    static readonly Color DockBarSurface = new Color(.20f, .30f, .34f);
    static Button DockButton(Transform parent, string name, string value) {
        var button = Button(parent, name, value);
        button.GetComponent<Image>().color = DockField;
        button.GetComponentInChildren<TMP_Text>(true).color = DockInk;
        return button;
    }
    static void BakeDock(Scene scene) {
        var roots = scene.GetRootGameObjects();
        var views = roots.SelectMany(r => r.GetComponentsInChildren<PartyLobbyView>(true)).ToArray();
        foreach (var lobby in roots.SelectMany(r => r.GetComponentsInChildren<MatchLobbyView>(true))) {
            var parent = lobby.transform.Find("MatchLayout") ?? lobby.transform.Find("OriginalMatchPanel/MatchLayout");
            if (!parent) throw new InvalidOperationException("Matching content layout missing");
            var existing = views.FirstOrDefault(v => v.matchLobby == lobby || !v.matchLobby);
            if (existing && existing.transform.parent != parent) Undo.SetTransformParent(existing.transform, parent, "Move party into matching page");
            var root = existing ? (RectTransform)existing.transform : Rect(parent, "PartyDock");
            BakeDock(root, lobby);
        }
    }
    static void BakeDock(RectTransform root, MatchLobbyView lobby) {
        Fill(root); Get<LayoutElement>(root).ignoreLayout = true;
        UiCanvasLayers.Apply(Get<Canvas>(root), UiCanvasLayers.Page, 0, false); Get<GraphicRaycaster>(root);
        var view = Get<PartyLobbyView>(root); view.matchLobby = lobby;
        var bar = Box(root, "Bar", DockBarSurface); view.bar = bar.gameObject;
        var r = bar.rectTransform; r.anchorMin = r.anchorMax = r.pivot = Vector2.up;
        r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(320, 148); bar.raycastTarget = true;
        foreach (string name in new[] { "Action", "Detail", "Accent" }) {
            var retired = r.Find(name); if (retired) Undo.DestroyObjectImmediate(retired.gameObject);
        }
        var edge = r.GetComponent<UnityEngine.UI.Outline>(); if (edge) Undo.DestroyObjectImmediate(edge);
        view.heading = Text(r, "Heading", "我的队伍 · 1/2", 20); view.heading.color = Color.white; Place(view.heading.rectTransform, 20, 10, 280, 30);
        view.memberButtons = new Button[5]; view.avatars = new Image[5]; view.memberLabels = new TMP_Text[5]; view.leaderCrowns = new GameObject[5];
        for (int i = 0; i < 5; i++) {
            var b = DockButton(r, "Member_" + i, ""); Place((RectTransform)b.transform, 20 + 86 * i, 48, 64, 64);
            var portrait = Box(b.transform, "Portrait", Color.white); Fill(portrait.rectTransform); portrait.sprite = ConfigManager.GetProfileSprite(1);
            var caption = Text(b.transform, "Status", i == 0 ? "队长" : "队员", 16); caption.color = Color.white;
            Place(caption.rectTransform, -8, 66, 80, 24); caption.alignment = TextAlignmentOptions.Center;
            var crown = Get<PartyLeaderCrown>(Rect(b.transform, "LeaderCrown")); crown.color = new Color(.71f, .53f, .22f); crown.raycastTarget = false;
            Place(crown.rectTransform, 52, -5, 24, 20); view.leaderCrowns[i] = crown.gameObject; crown.gameObject.SetActive(i == 0);
            view.memberButtons[i] = b; view.avatars[i] = portrait; view.memberLabels[i] = caption; b.gameObject.SetActive(i == 0);
        }
        view.invite = DockButton(r, "Invite", "+"); Place((RectTransform)view.invite.transform, 106, 48, 64, 64);
        view.invite.GetComponentInChildren<TMP_Text>().fontSize = 28;
        var menu = Box(r, "MemberMenu", DockSurface); view.memberMenu = menu.gameObject; Place(menu.rectTransform, 0, -216, 260, 210); menu.raycastTarget = true;
        view.menuHeading = Text(menu.transform, "Name", "成员", 19); view.menuHeading.color = DockInk; Place(view.menuHeading.rectTransform, 16, 8, 190, 30);
        view.closeMenu = DockButton(menu.transform, "Close", "×"); Place((RectTransform)view.closeMenu.transform, 220, 8, 30, 30);
        view.transfer = DockButton(menu.transform, "Transfer", "转让队长"); Place((RectTransform)view.transfer.transform, 16, 46, 228, 40);
        view.kick = DockButton(menu.transform, "Kick", "移出队伍"); Place((RectTransform)view.kick.transform, 16, 96, 228, 40);
        view.leave = DockButton(menu.transform, "Leave", "离开队伍"); Place((RectTransform)view.leave.transform, 16, 146, 228, 40);
        menu.gameObject.SetActive(false);
        view.ApplyCompactLayout();
        bar.gameObject.SetActive(false);
        EditorUtility.SetDirty(view);
    }
    static void BakeMatch(Scene scene) {
        foreach (var lobby in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MatchLobbyView>(true))) {
            var pages = ReadArray(lobby, "rulePages");
            if (pages.Length < 8) {
                var page = Object.Instantiate((GameObject)pages[3], ((GameObject)pages[3]).transform.parent, false); page.name = "SichuanDoubles";
                foreach (var card in page.GetComponentsInChildren<MatchButton>(true)) {
                    var so = new SerializedObject(card); so.FindProperty("rule").enumValueIndex = (int)MatchButton.MatchRule.SichuanDoubles; so.ApplyModifiedPropertiesWithoutUndo();
                    Set(card, "lobbyView", lobby);
                    var mode = Ref(card, "modeLabel") as TMP_Text; if (mode) mode.text = "双人组队 · 半庄8副";
                    var tier = Ref(card, "tierLabel") as TMP_Text; if (tier) tier.text = "Elo 匹配";
                }
                page.SetActive(false); Array(lobby, "rulePages", pages.Concat(new Object[] { page }).ToArray());
            }
            var buttons = ReadArray(lobby, "variantButtons");
            if (buttons.Length < 3) {
                var b = Object.Instantiate((Button)buttons[0], ((Button)buttons[0]).transform.parent, false); b.name = "Variant_2"; b.onClick = new Button.ButtonClickedEvent();
                Array(lobby, "variantButtons", buttons.Concat(new Object[] { b }).ToArray());
                Array(lobby, "selectedVariants", ReadArray(lobby, "selectedVariants").Concat(new Object[] { b.transform.Find("SelectedBackground").gameObject }).ToArray());
                Array(lobby, "variantLabels", ReadArray(lobby, "variantLabels").Concat(new Object[] { b.transform.Find("Label").GetComponent<TMP_Text>() }).ToArray());
                Array(lobby, "variantPlayerCounts", ReadArray(lobby, "variantPlayerCounts").Concat(new Object[] { b.transform.Find("PlayerCount/Count").GetComponent<TMP_Text>() }).ToArray());
                b.gameObject.SetActive(false);
            }
            var entries = lobby.GetComponentsInChildren<MatchButton>(true).Where(c => c.IsAvailable).Cast<Object>().ToArray();
            Array(lobby, "entries", entries); var panel = lobby.GetComponent<MatchPanel>(); if (panel) Array(panel, "matchButtons", entries);
        }
    }
    public static void Validate(Scene scene) {
        var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PartyNetworkManager>(true)).ToArray();
        if (components.Length != 1 || !components[0].friendPicker || !components[0].incomingPanel) throw new InvalidOperationException("Party references missing");
        foreach (var lobby in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MatchLobbyView>(true)))
            if (ReadArray(lobby, "rulePages").Length != 8) throw new InvalidOperationException("Doubles page missing");
        foreach (var view in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PartyLobbyView>(true)))
            if (!view.matchLobby || !view.transform.IsChildOf(view.matchLobby.transform) || view.leaderCrowns == null
                || view.leaderCrowns.Length != view.memberButtons.Length || view.leaderCrowns.Any(c => !c))
                throw new InvalidOperationException("Party panel must be inside matching with a crown for every slot");
        PlatformCanvasLayerBaker.Validate(scene);
    }
}
#endif
