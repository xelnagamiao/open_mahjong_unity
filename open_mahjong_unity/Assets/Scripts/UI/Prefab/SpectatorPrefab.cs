using System.Collections;
using System.Collections.Generic;
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

    public void InitializeSpectatorItem(string rule, string player1_name, string player2_name, string player3_name, string player4_name, string gamestate_id) {
        string ruleDisplay = "规则:" + RuleNameDictionary.GetWholeName(rule);
        if (RuleText != null) RuleText.text = ruleDisplay;
        if (Player1NameText != null) Player1NameText.text = $"玩家1: {player1_name ?? "-"}";
        if (Player2NameText != null) Player2NameText.text = $"玩家2: {player2_name ?? "-"}";
        if (Player3NameText != null) Player3NameText.text = $"玩家3: {player3_name ?? "-"}";
        if (Player4NameText != null) Player4NameText.text = $"玩家4: {player4_name ?? "-"}";
        if (GamestateIdText != null) GamestateIdText.text = $"游戏ID: {gamestate_id}";
        this.gamestate_id = gamestate_id;
    }

    private void Awake() {
        SpectateButton.onClick.AddListener(OnSpectateButtonClick);
    }

    private void OnSpectateButtonClick() {
        // 发送添加观战请求
        GameStateNetworkManager.Instance.AddSpectator(gamestate_id);
    }
}

