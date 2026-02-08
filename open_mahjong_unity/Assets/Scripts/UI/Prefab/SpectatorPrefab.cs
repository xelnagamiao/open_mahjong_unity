using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SpectatorPrefab : MonoBehaviour {
    [SerializeField] private TextMeshProUGUI RuleText;
    [SerializeField] private TextMeshProUGUI Player1IdText;
    [SerializeField] private TextMeshProUGUI Player2IdText;
    [SerializeField] private TextMeshProUGUI Player3IdText;
    [SerializeField] private TextMeshProUGUI Player4IdText;
    [SerializeField] private TextMeshProUGUI GamestateIdText;
    [SerializeField] private Button SpectateButton;
    
    private string gamestate_id;

    public void InitializeSpectatorItem(string rule, int player1_id, int player2_id, int player3_id, int player4_id, string gamestate_id) {
        RuleText.text = rule;
        if (rule == "guobiao") {
            RuleText.text = "规则:国标";
        } else if (rule == "qingque") {
            RuleText.text = "规则:青雀";
        } else if (rule == "riichi") {
            RuleText.text = "规则:立直";
        }
        Player1IdText.text = $"玩家1: {player1_id}";
        Player2IdText.text = $"玩家2: {player2_id}";
        Player3IdText.text = $"玩家3: {player3_id}";
        Player4IdText.text = $"玩家4: {player4_id}";
        if (GamestateIdText != null) {
            GamestateIdText.text = $"游戏ID: {gamestate_id}";
        }
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

