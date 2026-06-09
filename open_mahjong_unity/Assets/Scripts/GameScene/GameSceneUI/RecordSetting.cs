using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecordSetting : MonoBehaviour {
    public static RecordSetting Instance { get; private set; }

    [Header("牌谱手牌展示")]
    [SerializeField] private TMP_Text showCardsModeText; // 平躺明牌展示
    [SerializeField] private TMP_Text showMoqieModeText; // 手摸切灰显


    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white;
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f);

    private bool isShowCardsMode = true;
    public bool IsShowCardsMode { get => isShowCardsMode; }

    private bool isShowMoqieMode = true;
    public bool IsShowMoqieMode { get => isShowMoqieMode; }

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    public void Initialize() {
        gameObject.SetActive(true);
        AddClickListener(showCardsModeText, ToggleShowCardsMode);
        AddClickListener(showMoqieModeText, ToggleShowMoqieMode);
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
        // 重建当前节点河牌，使已有牌面的手摸切灰显即时刷新
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr != null) {
            recordMgr.GotoAction(recordMgr.currentNode);
        }
    }

    private void RefreshUI() {
        showCardsModeText.color = isShowCardsMode ? trueColor : falseColor;
        if (showMoqieModeText != null) {
            showMoqieModeText.color = isShowMoqieMode ? trueColor : falseColor;
        }
    }

    private void AddClickListener(TMP_Text text, UnityEngine.Events.UnityAction action) {
        Button button = text.GetComponent<Button>();
        if (button == null) {
            button = text.gameObject.AddComponent<Button>();
        }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}
