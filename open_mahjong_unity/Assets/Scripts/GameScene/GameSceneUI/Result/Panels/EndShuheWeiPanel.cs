using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EndShuheWeiPanel : MonoBehaviour {
    private const string StateNone = "";
    private const string StateGame = "gamestate";
    private const string StateRecord = "recordstate";
    private const float RevealInterval = 0.5f;
    private const float DetailNumberAnimationSeconds = 0.5f;
    private const float DetailPauseSeconds = 0.5f;

    public static EndShuheWeiPanel Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private GameObject FanCountPrefab;
    [SerializeField] private Button EndButton;
    [SerializeField] private TextMeshProUGUI EndButtonText;
    [SerializeField] private List<ShuheweiPlayerPanel> playerPanels = new List<ShuheweiPlayerPanel>();

    private Coroutine showRoutine;
    private Coroutine countdownRoutine;
    private string currentState = StateNone;
    private Dictionary<string, int> posToIndex = new Dictionary<string, int>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (EndButton != null) {
            EndButton.onClick.AddListener(OnClickEndButton);
        }
        CollectPlayerPanels();
        ResetReadyStatus();
    }

    private void OnEnable() {
        ResetReadyStatus();
    }

    public void ShowShuhewei(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        Dictionary<int, string> indexToPosition,
        Dictionary<string, PlayerInfoClass> player_to_info,
        bool isRecord = false
    ) {
        PrepareShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info, isRecord);
        PlayPreparedShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, isRecord);
    }

    public void PrepareShuhewei(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        Dictionary<int, string> indexToPosition,
        Dictionary<string, PlayerInfoClass> player_to_info,
        bool isRecord = false
    ) {
        if (showRoutine != null) {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }
        if (countdownRoutine != null) {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }

        currentState = isRecord ? StateRecord : StateGame;
        CollectPlayerPanels();
        ResetReadyStatus();
        if (titleText != null) {
            titleText.text = "数和尾";
        }
        if (EndButton != null) {
            EndButton.interactable = false;
            EndButton.gameObject.SetActive(true);
        }
        gameObject.SetActive(true);

        posToIndex.Clear();
        foreach (var kvp in indexToPosition) {
            posToIndex[kvp.Value] = kvp.Key;
        }

        InitializeAllPanels(player_to_info);
    }

    public void PlayPreparedShuhewei(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        bool isRecord = false
    ) {
        showRoutine = StartCoroutine(PlayReveal(player_fu, player_to_score, score_changes, player_fan, player_fu_types, isRecord));
    }

    private IEnumerator PlayReveal(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        bool isRecord
    ) {
        string[] order = { "self", "left", "top", "right" };
        for (int i = 0; i < order.Length; i++) {
            string pos = order[i];
            if (!posToIndex.ContainsKey(pos)) {
                continue;
            }
            int playerIndex = posToIndex[pos];
            string[] fanList = player_fan != null && player_fan.ContainsKey(playerIndex) ? player_fan[playerIndex] : Array.Empty<string>();
            string[] fuTypeList = player_fu_types != null && player_fu_types.ContainsKey(playerIndex) ? player_fu_types[playerIndex] : Array.Empty<string>();
            int displayFu = ShuheweiPlayerPanel.SumFuValues(fuTypeList);
            int displayFan = ShuheweiPlayerPanel.SumFanValues(fanList);
            int displayFuPoint = ShuheweiPlayerPanel.CalculateRoundFuPoint(displayFu, displayFan);

            ShuheweiPlayerPanel panel = GetPanelByKey(pos);
            yield return StartCoroutine(panel.PlayFuAndFanReveal(
                fuTypeList,
                fanList,
                FanCountPrefab,
                DetailNumberAnimationSeconds,
                DetailPauseSeconds
            ));
            panel.SetRoundStats(displayFu, displayFan, displayFuPoint);
            int totalScore = player_to_score.ContainsKey(playerIndex) ? player_to_score[playerIndex] : 0;
            int change = score_changes.ContainsKey(playerIndex) ? score_changes[playerIndex] : 0;
            panel.SetTotalScore(totalScore, change);
            yield return new WaitForSeconds(RevealInterval);
        }

        if (isRecord) {
            if (EndButton != null) {
                EndButton.interactable = true;
            }
            if (EndButtonText != null) {
                EndButtonText.text = "确认";
            }
            yield break;
        }

        if (EndButton != null) {
            EndButton.interactable = true;
        }
        countdownRoutine = StartCoroutine(CountDownAndHide(Mathf.RoundToInt(RoundEndTiming.HuConfirmCountdownSeconds)));
    }

    private IEnumerator CountDownAndHide(int seconds) {
        for (int i = seconds; i > 0; i--) {
            if (EndButtonText != null) {
                EndButtonText.text = $"确定({i})";
            }
            yield return new WaitForSeconds(1f);
        }
        if (EndButtonText != null) {
            EndButtonText.text = "确定(0)";
        }
        if (EndButton != null) {
            EndButton.interactable = false;
        }
        gameObject.SetActive(false);
    }

    private void InitializeAllPanels(Dictionary<string, PlayerInfoClass> player_to_info) {
        for (int i = 0; i < playerPanels.Count; i++) {
            ShuheweiPlayerPanel panel = playerPanels[i];
            string posKey = GetPositionKey(panel.Position);
            panel.SetUserName(player_to_info.ContainsKey(posKey) ? player_to_info[posKey].username : "");
            panel.ResetRoundStats();
            panel.SetReady(false);
            panel.ClearFanContainer();
        }
    }

    private void ResetReadyStatus() {
        for (int i = 0; i < playerPanels.Count; i++) {
            if (playerPanels[i] == null) {
                continue;
            }
            playerPanels[i].HideReadyIndicators();
        }
    }

    private ShuheweiPlayerPanel GetPanelByKey(string posKey) {
        for (int i = 0; i < playerPanels.Count; i++) {
            if (GetPositionKey(playerPanels[i].Position) == posKey) {
                return playerPanels[i];
            }
        }
        throw new Exception($"未配置位置面板: {posKey}");
    }

    private string GetPositionKey(ShuheweiPanelPosition position) {
        if (position == ShuheweiPanelPosition.Self) return "self";
        if (position == ShuheweiPanelPosition.Left) return "left";
        if (position == ShuheweiPanelPosition.Top) return "top";
        return "right";
    }

    public void UpdateReadyStatus(Dictionary<int, bool> playerToReady) {
        if (NormalGameStateManager.Instance == null) {
            return;
        }
        foreach (var kvp in playerToReady) {
            if (!NormalGameStateManager.Instance.indexToPosition.ContainsKey(kvp.Key)) {
                continue;
            }
            string position = NormalGameStateManager.Instance.indexToPosition[kvp.Key];
            bool isReady = kvp.Value;
            GetPanelByKey(position).SetReady(isReady);
        }
    }

    private void OnClickEndButton() {
        if (EndButton != null) {
            EndButton.interactable = false;
        }
        if (currentState == StateRecord) {
            gameObject.SetActive(false);
            GameRecordManager.Instance.AdvanceToNextAction();
            return;
        }
        if (currentState == StateGame) {
            GameStateNetworkManager.Instance.SendAction("ready", 0);
        }
    }

    public void ClearEndShuheWeiPanel() {
        if (showRoutine != null) {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }
        if (countdownRoutine != null) {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }
        currentState = StateNone;
        gameObject.SetActive(false);
        if (EndButton != null) {
            EndButton.interactable = false;
        }
        if (EndButtonText != null) {
            EndButtonText.text = "确定";
        }
        ResetReadyStatus();
        for (int i = 0; i < playerPanels.Count; i++) {
            ShuheweiPlayerPanel panel = playerPanels[i];
            if (panel == null) {
                continue;
            }
            panel.ResetRoundStats();
            panel.ClearFanContainer();
        }
    }

    private void CollectPlayerPanels() {
        ShuheweiPlayerPanel[] foundPanels = GetComponentsInChildren<ShuheweiPlayerPanel>(true);
        if (foundPanels == null || foundPanels.Length == 0) {
            return;
        }
        playerPanels.Clear();
        for (int i = 0; i < foundPanels.Length; i++) {
            if (foundPanels[i] != null) {
                playerPanels.Add(foundPanels[i]);
            }
        }
    }
}
