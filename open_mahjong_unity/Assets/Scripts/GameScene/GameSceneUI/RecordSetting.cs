using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecordSetting : MonoBehaviour {
    public static RecordSetting Instance { get; private set; }

    [Header("牌谱手牌展示")]
    [SerializeField] private TMP_Text showCardsModeText; // 平躺明牌展示


    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white;
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f);

    private bool isShowCardsMode = true;
    public bool IsShowCardsMode { get => isShowCardsMode; }

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
        RefreshUI();
        Game3DManager.Instance.RefreshRecordHandDisplay();
    }

    private void ToggleShowCardsMode() {
        isShowCardsMode = !isShowCardsMode;
        RefreshUI();
        Game3DManager.Instance.RefreshRecordHandDisplay();
    }

    private void RefreshUI() {
        showCardsModeText.color = isShowCardsMode ? trueColor : falseColor;
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
