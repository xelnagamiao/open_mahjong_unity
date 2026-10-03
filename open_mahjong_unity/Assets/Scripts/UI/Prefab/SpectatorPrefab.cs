using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SpectatorPrefab : MonoBehaviour {
    [SerializeField] private TextMeshProUGUI RuleText;
    [SerializeField] private TextMeshProUGUI Player1NameText;
    [SerializeField] private TextMeshProUGUI Player2NameText;
    [SerializeField] private TextMeshProUGUI Player3NameText;
    [SerializeField] private TextMeshProUGUI Player4NameText;
    [SerializeField] private TextMeshProUGUI GamestateIdText;
    [SerializeField] private Button SpectateButton;

    private string gamestate_id;

    public void InitializeSpectatorItem(string rule, string subRule, string player1_name, string player2_name, string player3_name, string player4_name, string gamestate_id) {
        string displayRule = string.IsNullOrEmpty(subRule) ? rule : subRule;
        string ruleDisplay = RuleNameDictionary.GetWholeName(displayRule);
        if (RuleText != null) RuleText.text = ruleDisplay;
        if (Player1NameText != null) Player1NameText.text = player1_name ?? "-";
        if (Player2NameText != null) Player2NameText.text = player2_name ?? "-";
        if (Player3NameText != null) Player3NameText.text = player3_name ?? "-";
        if (Player4NameText != null) Player4NameText.text = player4_name ?? "-";
        if (GamestateIdText != null) GamestateIdText.text = $"游戏 ID  {gamestate_id}";
        SpectateButton.interactable = !string.IsNullOrWhiteSpace(gamestate_id);
        this.gamestate_id = gamestate_id;
    }

    private void Awake() {
        SpectateButton.onClick.AddListener(OnSpectateButtonClick);
    }

    private void OnDestroy() {
        SpectateButton.onClick.RemoveListener(OnSpectateButtonClick);
    }

    private void OnSpectateButtonClick() {
        if (!string.IsNullOrWhiteSpace(gamestate_id) && GameStateNetworkManager.Instance != null) {
            GameStateNetworkManager.Instance.AddSpectator(gamestate_id);
        }
    }
}
