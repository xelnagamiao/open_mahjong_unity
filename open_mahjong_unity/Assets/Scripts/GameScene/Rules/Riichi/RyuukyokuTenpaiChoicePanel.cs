using UnityEngine;

public class RyuukyokuTenpaiChoicePanel : MonoBehaviour {
    public static RyuukyokuTenpaiChoicePanel Instance { get; private set; }

    [SerializeField] private GameObject root;
    [SerializeField] private RyuukyokuTenpaiChoiceButton tenpaiButton;
    [SerializeField] private RyuukyokuTenpaiChoiceButton notenButton;
    [SerializeField] private Vector2 panelOffset = new Vector2(40f, -40f);
    [SerializeField] private Color backgroundColor = new Color(0.94f, 0.93f, 0.89f, 1f);

    private bool _declareTenpai = true;
    private string _roundToken = "";
    private bool _initialized;

    private void Awake() {
        EnsureInitialized();
    }

    private void EnsureInitialized() {
        if (_initialized) return;
        _initialized = true;
        Instance = this;
        if (root == null) root = gameObject;
        // 仅初始化时应用，反复 ShowChoice 不累加偏移；兼容现有场景引用。
        if (root.transform is RectTransform rect) rect.anchoredPosition += panelOffset;
        UnityEngine.UI.Image background = root.GetComponent<UnityEngine.UI.Image>();
        if (background == null) background = root.AddComponent<UnityEngine.UI.Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;
        tenpaiButton.Button.onClick.AddListener(ChooseTenpai);
        notenButton.Button.onClick.AddListener(ChooseNoten);
        root.SetActive(false);
    }

    public void ShowChoice() {
        EnsureInitialized();
        ResetSelectionIfRoundChanged();
        RefreshButtons();
        root.SetActive(true);
    }

    public void Hide() {
        EnsureInitialized();
        root.SetActive(false);
    }

    public void ResetSelectionForRound() {
        EnsureInitialized();
        _declareTenpai = true;
        _roundToken = BuildRoundToken();
        RefreshButtons();
    }

    private void ChooseTenpai() {
        SetDeclareTenpai(true);
    }

    private void ChooseNoten() {
        SetDeclareTenpai(false);
    }

    private void SetDeclareTenpai(bool declareTenpai) {
        _declareTenpai = declareTenpai;
        RefreshButtons();
        GameStateNetworkManager.Instance.SetRyuukyokuTenpai(_declareTenpai);
    }

    private void RefreshButtons() {
        tenpaiButton.SetSelected(_declareTenpai);
        notenButton.SetSelected(!_declareTenpai);
    }

    private void ResetSelectionIfRoundChanged() {
        string token = BuildRoundToken();
        if (_roundToken == token) return;
        _roundToken = token;
        _declareTenpai = true;
        RefreshButtons();
    }

    private string BuildRoundToken() {
        RiichiGameState state = RiichiGameState.Active;
        if (state == null) return "";
        return $"{TableMirror.Current.CurrentRound}:{state.Honba}";
    }
}
