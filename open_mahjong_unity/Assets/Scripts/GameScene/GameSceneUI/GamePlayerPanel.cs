using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GamePlayerPanel : MonoBehaviour {
    [Header("玩家信息UI组件")]
    [SerializeField] private TMP_Text playerNameText;        // 玩家名称文本
    [SerializeField] private TMP_Text playerTitleText;       // 玩家头衔文本
    [SerializeField] private Image playerProfilePicture;     // 玩家头像
    [SerializeField] private Image playerProfileEdgePicture;   // 玩家头像边框
    [SerializeField] private Image playerIslossconnPicture; // 玩家是否掉线图片
    [SerializeField] private GameObject playerIsPeidaPicture; // 玩家是否陪打图片
    [SerializeField] private GameObject playerLangyongBadge; // 浪涌麻将：鸣牌次数标记（tag: langyong_N）
    [SerializeField] private TMP_Text playerLangyongCountText; // 浪涌鸣牌次数文字（可选）
    [SerializeField] private GameObject playerHuOrderBadge; // 四川血战：和牌顺序（first_hu/second_hu/third_hu）
    [SerializeField] private TMP_Text playerHuOrderText;
    [SerializeField] private Button GoToRecordSelectButton; // 牌谱模式下切换到该玩家视角

    [Header("四川·定缺标记")]
    [SerializeField] private Image playerDingqueImage;   // 定缺底图（按花色变色）
    [SerializeField] private TMP_Text playerDingqueText; // 定缺文字（缺万/缺饼/缺条）

    [Header("表情包显示")]
    [SerializeField] private Transform showStickerPos;   // 弹出表情锚点

    private const float StickerPopInDuration = 0.25f;
    private const float StickerSettleDuration = 0.1f;
    private const float StickerHoldDuration = 2f;
    private const float StickerFadeDuration = 0.5f;
    private const float StickerDisplaySize = 140f;

    private Coroutine _stickerCoroutine;

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
    }

    private void OnDisable() {
        ClearSticker();
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


    public void SetPlayerInfo(PlayerInfo playerInfo, string state, string position = null) {
        if (state == "gamestate") {
            playerNameText.text = StreamerModeHelper.FormatGamestatePlayerName(
                playerInfo.username, position, playerInfo.user_id);
        } else {
            playerNameText.text = playerInfo.username;
        }
        // 设置头衔
        playerTitleText.text = ConfigManager.GetTitleText(playerInfo.title_used);


        if (playerProfilePicture != null) {
            // 加载头像
            Sprite profileSprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.profile_used}");
            if (profileSprite != null) {
                playerProfilePicture.sprite = profileSprite;
            }

            // 设置 ProfileOnClick 的 user_id
            ProfileOnClick profileOnClick = playerProfilePicture.gameObject.GetComponent<ProfileOnClick>();
            if (profileOnClick != null) {
                profileOnClick.user_id = playerInfo.user_id;
            }
        }

        UpdateTagList(playerInfo.tag_list);

        // 牌谱模式下启用“切换视角”按钮；对局模式隐藏
        if (GoToRecordSelectButton != null) {
            bool isRecordState = state == "record";
            GoToRecordSelectButton.gameObject.SetActive(isRecordState);
            GoToRecordSelectButton.onClick.RemoveAllListeners();
            if (isRecordState) {
                int userId = playerInfo.user_id;
                GoToRecordSelectButton.onClick.AddListener(() => {
                    if (GameRecordManager.Instance != null) {
                        GameRecordManager.Instance.SwitchRecordPerspectiveToUser(userId);
                    }
                });
            }
        }
    }

    // 更新标签列表显示（立直/振听由对局内其他 UI 表现，此处处理掉线、陪打、浪涌鸣牌次数等）
    public void UpdateTagList(string[] tag_list) {
        playerIslossconnPicture.gameObject.SetActive(false);
        playerIsPeidaPicture.gameObject.SetActive(false);
        if (playerLangyongBadge != null) playerLangyongBadge.SetActive(false);
        if (playerLangyongCountText != null) playerLangyongCountText.text = "";
        if (playerHuOrderBadge != null) playerHuOrderBadge.SetActive(false);
        if (playerHuOrderText != null) playerHuOrderText.text = "";

        if (tag_list != null) {
            int langyongCount = -1;
            string huOrderLabel = null;
            foreach(var item in tag_list) {
                if (item == "offline") {
                    playerIslossconnPicture.gameObject.SetActive(true);
                }
                if (item == "peida") {
                    playerIsPeidaPicture.gameObject.SetActive(true);
                }
                if (item == "first_hu") huOrderLabel = "一和";
                else if (item == "second_hu") huOrderLabel = "二和";
                else if (item == "third_hu") huOrderLabel = "三和";
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
            if (!string.IsNullOrEmpty(huOrderLabel)) {
                if (playerHuOrderBadge != null) playerHuOrderBadge.SetActive(true);
                if (playerHuOrderText != null) playerHuOrderText.text = huOrderLabel;
            }
        }
    }

    /// <summary>在 showStickerPos 弹出表情（sticker 格式 pack/id，如 turtle/3）。</summary>
    public void ShowSticker(string stickerPath) {
        if (showStickerPos == null || string.IsNullOrEmpty(stickerPath)) return;
        ClearSticker();

        Sprite sprite = Resources.Load<Sprite>($"image/sticker/{stickerPath}");
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
        if (GoToRecordSelectButton != null) GoToRecordSelectButton.gameObject.SetActive(false);
    }
}
