using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GamePlayerPanel : MonoBehaviour {
    public const string ActionMenuName = "PlayerActionMenu";

    [Header("玩家信息UI组件")]
    [SerializeField] private TMP_Text playerNameText;        // 玩家名称文本
    [SerializeField] private TMP_Text playerTitleText;       // 玩家头衔文本
    [SerializeField] private Image playerProfilePicture;     // 玩家头像
    [SerializeField] private Image playerProfileEdgePicture;   // 玩家头像边框
    [SerializeField] private Image playerIslossconnPicture; // 玩家是否掉线图片
    [SerializeField] private GameObject playerIsPeidaPicture; // 玩家是否陪打图片
    [SerializeField] private GameObject playerLangyongBadge; // 浪涌麻将：鸣牌次数标记（tag: langyong_N）
    [SerializeField] private TMP_Text playerLangyongCountText; // 浪涌鸣牌次数文字（可选）
    [SerializeField] private Button GoToRecordSelectButton; // 兼容旧场景引用，独立“转到”按钮始终隐藏

    [Header("四川·定缺标记")]
    [SerializeField] private Image playerDingqueImage;   // 定缺底图（按花色变色）
    [SerializeField] private TMP_Text playerDingqueText; // 定缺文字（缺万/缺饼/缺条）

    [Header("玩家共通操作菜单")]
    [SerializeField] private GameObject actionMenu;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private Button recordPerspectiveButton;
    [SerializeField] private Image muteButtonImage;
    [SerializeField] private TMP_Text muteButtonLabel;
    [SerializeField] private Sprite stickerVisibleSprite;
    [SerializeField] private Sprite stickerMutedSprite;

    [Header("表情包显示")]
    [SerializeField] private Transform showStickerPos;   // 弹出表情锚点

    private const float StickerPopInDuration = 0.25f;
    private const float StickerSettleDuration = 0.1f;
    private const float StickerHoldDuration = 2f;
    private const float StickerFadeDuration = 0.5f;
    private const float StickerDisplaySize = 140f;

    private Coroutine _stickerCoroutine;
    [SerializeField] private TMP_Text duplicateRemainingTilesText;
    private int duplicateOriginalPlayerIndex = -1;
    [SerializeField] private RecordPlayerWaits recordWaits;
    public RecordPlayerWaits RecordWaits => recordWaits;
    private PlayerInfo titlePlayerInfo;
    private string titleDisplayState;

    private void OnEnable() {
        GameSettings.TitleChanged += RefreshTitle;
        GameSettings.AppearanceChanged += RefreshAppearance;
        RefreshTitle(0, 1);
    }

    private void RefreshTitle(int userId, int titleId) {
        if (titlePlayerInfo == null || playerTitleText == null) return;
        if (userId == titlePlayerInfo.user_id && titleDisplayState == "gamestate") titlePlayerInfo.title_used = titleId;
        playerTitleText.richText = false;
        playerTitleText.text = GameSettings.Current.GetTitleText(titlePlayerInfo.title_used);
    }

    private void RefreshAppearance(InventoryAppearance appearance) {
        if (titlePlayerInfo == null || playerProfilePicture == null) return;
        if (appearance != null && appearance.user_id == titlePlayerInfo.user_id && titleDisplayState == "gamestate") {
            titlePlayerInfo.profile_used = appearance.profile_image_id;
            titlePlayerInfo.character_used = appearance.character_id;
            titlePlayerInfo.voice_used = appearance.voice_id;
            titlePlayerInfo.avatar_frame_used = appearance.avatar_frame_id;
            if (NormalGameStateManager.Instance != null) {
                foreach (var info in NormalGameStateManager.Instance.player_to_info.Values) {
                    if (info.userId != appearance.user_id) continue;
                    info.profile_used = appearance.profile_image_id;
                    info.character_used = appearance.character_id;
                    info.voice_used = appearance.voice_id;
                    info.avatar_frame_used = appearance.avatar_frame_id;
                }
            }
        }
        playerProfilePicture.sprite = GameSettings.Current.GetProfileSprite(titlePlayerInfo.profile_used);
        ApplyAvatarFrame(titlePlayerInfo.avatar_frame_used);
    }

    private void ApplyAvatarFrame(int itemId) {
        AvatarFrameGraphic.Apply(playerProfilePicture, itemId);
        // The legacy square backing would show around a thinner, rounded frame.
        // Keep its click target, but let the equipped cosmetic define the silhouette.
        if (playerProfileEdgePicture != null)
            playerProfileEdgePicture.canvasRenderer.SetAlpha(GameSettings.Current.GetAvatarFrameColor(itemId).a > 0 ? 0 : 1);
    }

    // 三种花色定缺显示：1=万 2=筒 3=条
    private static readonly string[] DingqueTexts = { "", "万", "筒", "条" };
    private static readonly Color[] DingqueColors = {
        Color.clear,
        new Color(0.85f, 0.25f, 0.25f), // 万：红
        new Color(0.25f, 0.60f, 0.95f), // 饼：蓝
        new Color(0.30f, 0.75f, 0.35f), // 条：绿
    };

    private void Awake() {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
        if (playerLangyongBadge != null) playerLangyongBadge.SetActive(false);
        SetDingque(0);
        EnsureShowStickerPos();
        EnsureActionMenu();
        HideActionMenu();
        if (playerProfileEdgePicture != null
            && playerProfileEdgePicture.GetComponent<PlayerPanelClickRelay>() == null) {
            playerProfileEdgePicture.gameObject.AddComponent<PlayerPanelClickRelay>();
        }
    }

    private void OnDisable() {
        recordWaits?.Hide();
        GameSettings.TitleChanged -= RefreshTitle;
        GameSettings.AppearanceChanged -= RefreshAppearance;
        ClearSticker();
        HideActionMenu();
    }

    private void EnsureShowStickerPos() {
        if (showStickerPos != null) return;
        GameObject anchor = new GameObject("ShowStickerPos", typeof(RectTransform));
        anchor.transform.SetParent(transform, false);
        RectTransform rt = anchor.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 72f);
        rt.sizeDelta = new Vector2(StickerDisplaySize, StickerDisplaySize);
        showStickerPos = anchor.transform;
    }

    /// <summary>
    /// 设置该玩家的定缺显示。suit: 1=万 2=饼 3=条，其余（含 0=未定缺）隐藏。
    /// 与 tag_list 类似，由 GameCanvas 在收到服务端定缺同步时统一调用。
    /// </summary>
    public void SetDingque(int suit) {
        bool show = suit >= 1 && suit <= 3;
        if (playerDingqueImage != null) playerDingqueImage.gameObject.SetActive(show);
        if (playerDingqueText != null) playerDingqueText.gameObject.SetActive(show);
        if (!show) return;
        if (playerDingqueText != null) playerDingqueText.text = DingqueTexts[suit];
        if (playerDingqueImage != null) playerDingqueImage.color = DingqueColors[suit];
    }

    /// <summary>复式每家只摸自己的牌山；按原始座位显示服务端余牌，0 也显示。</summary>
    public void RefreshDuplicateRemainingTiles() {
        int? remaining = GameSession.Current.IsDuplicate && GameSession.Current.RoomRule == "guobiao"
            ? TableMirror.Current.GetDuplicateRemainingTiles(duplicateOriginalPlayerIndex)
            : null;
        if (!remaining.HasValue) {
            if (duplicateRemainingTilesText != null) duplicateRemainingTilesText.transform.parent.gameObject.SetActive(false);
            return;
        }
        duplicateRemainingTilesText.transform.parent.gameObject.SetActive(true);
        duplicateRemainingTilesText.text = $"余牌 {remaining.Value}";
        duplicateRemainingTilesText.color = remaining.Value <= 3
            ? new Color(1f, 0.67f, 0.4f)
            : new Color(0.94f, 0.97f, 1f);
    }

#if UNITY_EDITOR
    public void BakeDuplicateRemainingTilesLabel() {
        if (duplicateRemainingTilesText != null) return;
        GameObject badge = new GameObject("DuplicateRemainingTiles", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        badge.layer = gameObject.layer;
        badge.transform.SetParent(transform, false);
        RectTransform rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        // 四家面板的头衔下方留白，不覆盖头像、用户名或玩家状态标记。
        rect.anchoredPosition = new Vector2(0f, -88f);
        rect.sizeDelta = new Vector2(120f, 28f);
        Image background = badge.GetComponent<Image>();
        background.color = new Color(0.07f, 0.13f, 0.16f, 0.85f);
        background.raycastTarget = false;

        GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = gameObject.layer;
        label.transform.SetParent(badge.transform, false);
        duplicateRemainingTilesText = label.GetComponent<TextMeshProUGUI>();
        RectTransform textRect = duplicateRemainingTilesText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        if (playerTitleText != null) duplicateRemainingTilesText.font = playerTitleText.font;
        else if (playerNameText != null) duplicateRemainingTilesText.font = playerNameText.font;
        duplicateRemainingTilesText.fontSize = 22f;
        duplicateRemainingTilesText.alignment = TextAlignmentOptions.Center;
        duplicateRemainingTilesText.enableWordWrapping = false;
        duplicateRemainingTilesText.raycastTarget = false;
    }
#endif


    public void SetPlayerInfo(PlayerInfo playerInfo, string state, string position = null) {
        if (state != "record") recordWaits?.Hide();
        duplicateOriginalPlayerIndex = state == "gamestate" ? playerInfo.original_player_index : -1;
        RefreshDuplicateRemainingTiles();
        if (state == "gamestate") {
            playerNameText.text = StreamerModeHelper.FormatGamestatePlayerName(
                playerInfo.username, position, playerInfo.user_id);
        } else if (state == "record" && RecordSetting.Instance != null && RecordSetting.Instance.IsAnonymousPlayers) {
            // 牌谱匿名玩家：按 original_player_index（0=东 1=南 2=西 3=北）显示"X起玩家"
            playerNameText.text = RecordSetting.GetAnonymousPlayerName(playerInfo.original_player_index);
        } else {
            playerNameText.text = playerInfo.username;
        }
        // 设置头衔
        titlePlayerInfo = playerInfo;
        titleDisplayState = state;
        RefreshTitle(0, 1);

        if (playerProfilePicture != null) {
            // 加载头像
            Sprite profileSprite = GameSettings.Current.GetProfileSprite(playerInfo.profile_used);
            if (profileSprite != null) {
                playerProfilePicture.sprite = profileSprite;
            }
            ApplyAvatarFrame(playerInfo.avatar_frame_used);

            ProfileOnClick profileOnClick = playerProfilePicture.gameObject.GetComponent<ProfileOnClick>();
            if (profileOnClick != null) {
                profileOnClick.user_id = playerInfo.user_id;
            }
        }

        BindActionMenuContext(playerInfo.user_id, state, position, playerInfo.original_player_index);
        HideActionMenu();

        UpdateTagList(playerInfo.tag_list);
    }

    // 更新标签列表显示（立直/振听由对局内其他 UI 表现，此处处理掉线、陪打、浪涌鸣牌次数等）
    public void UpdateTagList(string[] tag_list, string roomRule = null) {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
        if (playerLangyongBadge != null) playerLangyongBadge.SetActive(false);
        if (playerLangyongCountText != null) playerLangyongCountText.text = "";

        if (tag_list != null) {
            if ((roomRule == "shanghai" || roomRule == "shanxi") && playerLangyongBadge != null) {
                var labels = new System.Collections.Generic.List<string>();
                var partners = new System.Collections.Generic.List<string>();
                foreach (string tag in tag_list) {
                    if (tag == "declared_ready") labels.Add(roomRule == "shanxi" ? "已报听" : "已敲牌");
                    if (tag != null && tag.StartsWith("chengbao_")
                        && int.TryParse(tag.Substring(9), out int seat) && seat >= 0 && seat < 4)
                        partners.Add(new[] { "东", "南", "西", "北" }[seat]);
                }
                if (partners.Count > 0) labels.Add("承包" + string.Join("", partners));
                playerLangyongBadge.SetActive(labels.Count > 0);
                if (playerLangyongCountText != null) playerLangyongCountText.text = string.Join(" · ", labels);
            }
            int langyongCount = -1;
            foreach(var item in tag_list) {
                if (item == "offline") {
                    playerIslossconnPicture.gameObject.SetActive(true);
                }
                if (item == "peida") {
                    playerIsPeidaPicture.gameObject.SetActive(true);
                }
                // langyong_wave 由 GameCanvas 全局显示；此处仅显示该玩家个人鸣牌次数 langyong_N
                if (item != null && item.StartsWith("langyong_") && item != "langyong_wave") {
                    if (int.TryParse(item.Substring("langyong_".Length), out int count)) {
                        langyongCount = count;
                    }
                }
            }
            if (langyongCount >= 0 && playerLangyongBadge != null) {
                playerLangyongBadge.SetActive(true);
                if (playerLangyongCountText != null) {
                    playerLangyongCountText.text = $"浪涌点数*{langyongCount}";
                }
            }
        }
    }

    /// <summary>在 showStickerPos 弹出表情（sticker 格式 pack/id，如 turtle/3）。</summary>
    public void ShowSticker(string stickerPath) {
        if (showStickerPos == null || string.IsNullOrEmpty(stickerPath)) return;
        ClearSticker();

        Sprite sprite = LoadStickerSprite(stickerPath);
        if (sprite == null) {
            Debug.LogWarning($"ShowSticker: 未找到资源 image/sticker/{stickerPath}");
            return;
        }

        GameObject stickerObj = new GameObject("StickerDisplay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        stickerObj.transform.SetParent(showStickerPos, false);
        RectTransform rt = stickerObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(StickerDisplaySize, StickerDisplaySize);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.zero;

        Image image = stickerObj.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = false;

        _stickerCoroutine = StartCoroutine(PopAndFadeSticker(stickerObj, image));
    }

    private static Sprite LoadStickerSprite(string stickerPath) {
        // 旧包名复用 turtle，Resources 中只保留一套乌龟表情图片。
        const string legacyPrefix = "guigui/";
        if (stickerPath.StartsWith(legacyPrefix, System.StringComparison.Ordinal)) {
            stickerPath = "turtle/" + stickerPath.Substring(legacyPrefix.Length);
        }
        return Resources.Load<Sprite>($"image/sticker/{stickerPath}");
    }

    /// <summary>停止协程并销毁 showStickerPos 下所有表情实例。</summary>
    public void ClearSticker() {
        if (_stickerCoroutine != null) {
            StopCoroutine(_stickerCoroutine);
            _stickerCoroutine = null;
        }
        if (showStickerPos == null) return;
        for (int i = showStickerPos.childCount - 1; i >= 0; i--) {
            Transform child = showStickerPos.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }
    }

    private IEnumerator PopAndFadeSticker(GameObject stickerObj, Image image) {
        if (stickerObj == null || image == null) yield break;

        float elapsed = 0f;
        while (elapsed < StickerPopInDuration) {
            if (stickerObj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / StickerPopInDuration);
            float scale = Mathf.Lerp(0f, 1.15f, EaseOutBack(t));
            stickerObj.transform.localScale = Vector3.one * scale;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < StickerSettleDuration) {
            if (stickerObj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / StickerSettleDuration);
            float scale = Mathf.Lerp(1.15f, 1f, t);
            stickerObj.transform.localScale = Vector3.one * scale;
            yield return null;
        }

        yield return new WaitForSeconds(StickerHoldDuration);

        Color originalColor = image.color;
        elapsed = 0f;
        while (elapsed < StickerFadeDuration) {
            if (stickerObj == null || image == null) yield break;
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / StickerFadeDuration);
            image.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            yield return null;
        }

        if (stickerObj != null) Destroy(stickerObj);
        _stickerCoroutine = null;
    }

    private static float EaseOutBack(float t) {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    public void Clear() {
        titlePlayerInfo = null;
        ApplyAvatarFrame(-1);
        duplicateOriginalPlayerIndex = -1;
        RefreshDuplicateRemainingTiles();
        playerNameText.text = "";
        playerTitleText.text = "";
        if (playerProfilePicture != null) {
            Sprite profileSprite = Resources.Load<Sprite>("image/Profiles/1");
            if (profileSprite != null) playerProfilePicture.sprite = profileSprite;
            ProfileOnClick profileOnClick = playerProfilePicture.GetComponent<ProfileOnClick>();
            if (profileOnClick != null) profileOnClick.user_id = 0;
        }
        UpdateTagList(null);
        SetDingque(0);
        ClearSticker();
        HideActionMenu();
        BindActionMenuContext(0, null, null);
    }
}
