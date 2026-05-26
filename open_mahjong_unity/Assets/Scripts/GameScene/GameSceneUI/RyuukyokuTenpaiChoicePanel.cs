using UnityEngine;

public class RyuukyokuTenpaiChoicePanel : MonoBehaviour {
    public static RyuukyokuTenpaiChoicePanel Instance { get; private set; }

    [SerializeField] private GameObject root;
    [SerializeField] private RyuukyokuTenpaiChoiceButton tenpaiButton;
    [SerializeField] private RyuukyokuTenpaiChoiceButton notenButton;

    private bool _declareTenpai = true;
    private string _roundToken = "";

    private void Awake() {
        Instance = this;
        if (root == null) root = gameObject;
        tenpaiButton.Button.onClick.AddListener(ChooseTenpai);
        notenButton.Button.onClick.AddListener(ChooseNoten);
        Hide();
    }

    public void ShowChoice() {
        ResetSelectionIfRoundChanged();
        RefreshButtons();
        root.SetActive(true);
    }

    public void Hide() {
        root.SetActive(false);
    }

    public void ResetSelectionForRound() {
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
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        if (gameManager == null) return "";
        return $"{gameManager.currentRound}:{gameManager.honba}";
    }
}
