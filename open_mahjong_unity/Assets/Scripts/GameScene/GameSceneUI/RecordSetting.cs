using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecordSetting : MonoBehaviour {
    public static RecordSetting Instance { get; private set; }

    [Header("牌谱手牌展示")]
    [SerializeField] private TMP_Text showCardsModeText; // 平躺明牌展示
    [SerializeField] private TMP_Text showMoqieModeText; // 手摸切灰显
    [SerializeField] private TMP_Text showChongHintText; // 铳牌提示

    [SerializeField] private TMP_Text showHepaiAnimationText;

    [SerializeField] private TMP_Text showAnonymousPlayersText; // 匿名玩家

    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white;
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f);

    private bool isShowCardsMode = true;
    public bool IsShowCardsMode { get => isShowCardsMode; }

    private bool isShowMoqieMode = true;
    public bool IsShowMoqieMode { get => isShowMoqieMode; }

    private bool isShowChongHint = true;
    public bool IsShowChongHint { get => isShowChongHint; }

    private bool isShowHepaiAnimation = false;
    public bool IsShowHepaiAnimation { get => isShowHepaiAnimation; }

    private bool isAnonymousPlayers = false;
    public bool IsAnonymousPlayers { get => isAnonymousPlayers; }

    // 牌谱：按 original_player_index（0~3）映射到 东/南/西/北 起玩家
    private static readonly string[] AnonymousPlayerNamesByOriginIndex = {
        "东起玩家",
        "南起玩家",
        "西起玩家",
        "北起玩家"
    };

    public static string GetAnonymousPlayerName(int originalPlayerIndex) {
        if (originalPlayerIndex < 0 || originalPlayerIndex >= AnonymousPlayerNamesByOriginIndex.Length) {
            return string.Empty;
        }
        return AnonymousPlayerNamesByOriginIndex[originalPlayerIndex];
    }

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    public void Initialize() {
        gameObject.SetActive(true);
        isShowChongHint = true;
        AddClickListener(showCardsModeText, ToggleShowCardsMode);
        AddClickListener(showMoqieModeText, ToggleShowMoqieMode);
        if (showChongHintText != null) {
            AddClickListener(showChongHintText, ToggleShowChongHint);
        }
        AddClickListener(showHepaiAnimationText, ToggleShowHepaiAnimation);
        if (showAnonymousPlayersText != null) {
            AddClickListener(showAnonymousPlayersText, ToggleAnonymousPlayers);
        }
        RefreshUI();
    }

    private void ToggleShowCardsMode() {
        isShowCardsMode = !isShowCardsMode;
        RefreshUI();
        Game3DManager.Instance.RefreshRecordHandDisplay();
    }

    private void ToggleShowMoqieMode() {
        isShowMoqieMode = !isShowMoqieMode;
        RefreshUI();
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr != null) {
            recordMgr.GotoAction(recordMgr.currentNode);
        }
    }

    private void ToggleShowChongHint() {
        isShowChongHint = !isShowChongHint;
        RefreshUI();
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr != null) {
            recordMgr.GotoAction(recordMgr.currentNode);
        }
    }

    private void ToggleShowHepaiAnimation() {
        isShowHepaiAnimation = !isShowHepaiAnimation;
        RefreshUI();
    }

    private void ToggleAnonymousPlayers() {
        isAnonymousPlayers = !isAnonymousPlayers;
        RefreshUI();
        // 仅刷新四角玩家昵称显示；牌面/操作区不变，无需 GotoAction
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr != null) {
            recordMgr.RefreshRecordPlayerPanelNames();
        }
    }

    private void RefreshUI() {
        showCardsModeText.color = isShowCardsMode ? trueColor : falseColor;
        if (showMoqieModeText != null) {
            showMoqieModeText.color = isShowMoqieMode ? trueColor : falseColor;
        }
        if (showChongHintText != null) {
            showChongHintText.color = isShowChongHint ? trueColor : falseColor;
        }
        if (showHepaiAnimationText != null) {
            showHepaiAnimationText.color = isShowHepaiAnimation ? trueColor : falseColor;
        }
        if (showAnonymousPlayersText != null) {
            showAnonymousPlayersText.color = isAnonymousPlayers ? trueColor : falseColor;
        }
    }

    private void AddClickListener(TMP_Text text, UnityEngine.Events.UnityAction action) {
        if (text == null) return;
        Button button = text.GetComponent<Button>();
        if (button == null) {
            button = text.gameObject.AddComponent<Button>();
        }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}
