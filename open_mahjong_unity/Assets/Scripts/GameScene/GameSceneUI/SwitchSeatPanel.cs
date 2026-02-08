using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SwitchSeatPanel : MonoBehaviour {
    public static SwitchSeatPanel Instance { get; private set; }
    [SerializeField] private TMP_Text NextRoundName;
    [SerializeField] private GameObject SelfPos;
    [SerializeField] private GameObject RightPos;
    [SerializeField] private GameObject TopPos;
    [SerializeField] private GameObject LeftPos;

    // 存储四个方向的面板（包含用户名文本）
    [SerializeField] private GameObject SelfPanel;
    [SerializeField] private GameObject RightPanel;
    [SerializeField] private GameObject TopPanel;
    [SerializeField] private GameObject LeftPanel;

    // 存储四个方向的名称文本（作为面板的子对象）
    [SerializeField] private TMP_Text SelfName;
    [SerializeField] private TMP_Text RightName;
    [SerializeField] private TMP_Text TopName;
    [SerializeField] private TMP_Text LeftName;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private int BackCurrentNum(int num){
        if (num == 0){
            return 3;
        } else {
            return num - 1;
        }
    }

    // Start is called before the first frame update
    public IEnumerator ShowSwitchSeatPanel(int gameRound){
        // 重置面板位置到初始位置
        ResetPanelPositions();
        // 获取自身索引
        int selfIndex = NormalGameStateManager.Instance.selfIndex;
        // 获取自身索引进1的索引(东南西北=>东 也就是上轮换位后的初始位)
        int selfNum = BackCurrentNum(selfIndex);

        // 获取四个方向的用户名
        SelfName.text = NormalGameStateManager.Instance.player_to_info["self"].username;
        RightName.text = NormalGameStateManager.Instance.player_to_info["right"].username;
        TopName.text = NormalGameStateManager.Instance.player_to_info["top"].username;
        LeftName.text = NormalGameStateManager.Instance.player_to_info["left"].username;

        Dictionary<int, string> indexToOriginalPosition = new Dictionary<int, string>();
        // 获取所有人的原始初始位
        if (selfNum == 0){
            indexToOriginalPosition[0] = "self";
            indexToOriginalPosition[1] = "right";
            indexToOriginalPosition[2] = "top";
            indexToOriginalPosition[3] = "left";
        } else if (selfNum == 1){
            indexToOriginalPosition[0] = "left";
            indexToOriginalPosition[1] = "self";
            indexToOriginalPosition[2] = "right";
            indexToOriginalPosition[3] = "top";
        } else if (selfNum == 2){
            indexToOriginalPosition[0] = "top";
            indexToOriginalPosition[1] = "left";
            indexToOriginalPosition[2] = "self";
            indexToOriginalPosition[3] = "right";
        } else if (selfNum == 3){
            indexToOriginalPosition[0] = "right";
            indexToOriginalPosition[1] = "top";
            indexToOriginalPosition[2] = "left";
            indexToOriginalPosition[3] = "self";
        }
        
        // 计算换位映射关系：从原始索引到目标索引
        Dictionary<int, int> indexMapping = new Dictionary<int, int>();

        if (gameRound == 5 || gameRound == 13){
            // 东位(0)与南位(1)互换，西位(2)与北位(3)互换
            indexMapping[0] = 1; // 东->南
            indexMapping[1] = 0; // 南->东
            indexMapping[2] = 3; // 西->北
            indexMapping[3] = 2; // 北->西
        } else if (gameRound == 9){
            // 东位(0)到西位(2)，南位(1)到北位(3)，西位(2)到南位(1)，北位(3)到东位(0)
            indexMapping[0] = 2; // 东->西
            indexMapping[1] = 3; // 南->北
            indexMapping[2] = 1; // 西->南
            indexMapping[3] = 0; // 北->东
        }

        // 计算位置映射：从当前位置到目标位置
        // 这个映射用于告诉动画系统每个UI位置的面板应该移动到哪里
        Dictionary<string, string> positionMapping = new Dictionary<string, string>();
        foreach (var kvp in indexToOriginalPosition) {
            // indexToOriginalPosition: 原始玩家索引 -> 当前在UI上显示的位置("self", "right", "top", "left")
            int originalIndex = kvp.Key;          // 原始玩家索引(0=东,1=南,2=西,3=北)
            string currentPosition = kvp.Value;   // 当前在UI上显示的位置

            // indexMapping: 原始玩家索引 -> 换位后的目标索引
            // 根据局数(gameRound)确定换位规则
            int targetIndex = indexMapping[originalIndex];

            // 通过目标索引找到目标位置：目标索引 -> 目标在UI上显示的位置
            string targetPosition = indexToOriginalPosition[targetIndex];

            // 最终映射：当前UI位置 -> 目标UI位置
            // 例如：如果"self"位置的玩家要换到"right"位置，那么positionMapping["self"] = "right"
            positionMapping[currentPosition] = targetPosition;
        }

        gameObject.SetActive(true);

        if (gameRound == 5){
            NextRoundName.text = "东圈=>南圈";
        } else if (gameRound == 9){
            NextRoundName.text = "南圈=>西圈";
        } else if (gameRound == 13){
            NextRoundName.text = "西圈=>北圈";
        }

        // 执行换位动画
        yield return StartCoroutine(PerformSwitchAnimation(positionMapping));

        // 等待一段时间让玩家观看结果
        yield return new WaitForSeconds(2);

        // 隐藏面板
        gameObject.SetActive(false);

    }

    // 执行换位动画
    private IEnumerator PerformSwitchAnimation(Dictionary<string, string> positionMapping) {
        // 收集所有需要移动的面板和它们的目标位置
        Dictionary<GameObject, Vector3> panelToTargetPos = new Dictionary<GameObject, Vector3>();

        foreach (var kvp in positionMapping) {
            string fromPosition = kvp.Key;
            string toPosition = kvp.Value;

            GameObject panel = GetPanelByPosition(fromPosition);
            Vector3 targetPos = GetTargetPosition(toPosition);

            panelToTargetPos[panel] = targetPos;
        }

        // 执行简单的移动动画
        float duration = 1.5f;
        float elapsedTime = 0f;

        // 记录起始位置
        Dictionary<GameObject, Vector3> panelToStartPos = new Dictionary<GameObject, Vector3>();
        foreach (var kvp in panelToTargetPos) {
            GameObject panel = kvp.Key;
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            if (rectTransform != null) {
                panelToStartPos[panel] = rectTransform.anchoredPosition;
            }
        }

        // 动画循环
        while (elapsedTime < duration) {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

            foreach (var kvp in panelToTargetPos) {
                GameObject panel = kvp.Key;
                RectTransform rectTransform = panel.GetComponent<RectTransform>();
                if (rectTransform != null) {
                    Vector3 startPos = panelToStartPos[panel];
                    Vector3 targetPos = kvp.Value;

                    rectTransform.anchoredPosition = Vector3.Lerp(startPos, targetPos, easedProgress);
                }
            }

            yield return null;
        }

        // 确保最终位置正确
        foreach (var kvp in panelToTargetPos) {
            GameObject panel = kvp.Key;
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            if (rectTransform != null) {
                Vector3 targetPos = kvp.Value;
                rectTransform.anchoredPosition = targetPos;
            }
        }
    }

    // 获取目标位置的anchoredPosition
    private Vector3 GetTargetPosition(string positionName) {
        switch (positionName) {
            case "self": return SelfPos.GetComponent<RectTransform>().anchoredPosition;
            case "right": return RightPos.GetComponent<RectTransform>().anchoredPosition;
            case "top": return TopPos.GetComponent<RectTransform>().anchoredPosition;
            case "left": return LeftPos.GetComponent<RectTransform>().anchoredPosition;
            default: return Vector3.zero;
        }
    }

    // 根据位置名称获取对应的面板组件
    private GameObject GetPanelByPosition(string position) {
        switch (position) {
            case "self": return SelfPanel;
            case "right": return RightPanel;
            case "top": return TopPanel;
            case "left": return LeftPanel;
            default: return null;
        }
    }

    // 重置面板位置到初始固定位置
    private void ResetPanelPositions() {
        if (SelfPanel != null && SelfPos != null) {
            RectTransform panelRect = SelfPanel.GetComponent<RectTransform>();
            RectTransform posRect = SelfPos.GetComponent<RectTransform>();
            if (panelRect != null && posRect != null) {
                panelRect.anchoredPosition = posRect.anchoredPosition;
            }
        }
        if (RightPanel != null && RightPos != null) {
            RectTransform panelRect = RightPanel.GetComponent<RectTransform>();
            RectTransform posRect = RightPos.GetComponent<RectTransform>();
            if (panelRect != null && posRect != null) {
                panelRect.anchoredPosition = posRect.anchoredPosition;
            }
        }
        if (TopPanel != null && TopPos != null) {
            RectTransform panelRect = TopPanel.GetComponent<RectTransform>();
            RectTransform posRect = TopPos.GetComponent<RectTransform>();
            if (panelRect != null && posRect != null) {
                panelRect.anchoredPosition = posRect.anchoredPosition;
            }
        }
        if (LeftPanel != null && LeftPos != null) {
            RectTransform panelRect = LeftPanel.GetComponent<RectTransform>();
            RectTransform posRect = LeftPos.GetComponent<RectTransform>();
            if (panelRect != null && posRect != null) {
                panelRect.anchoredPosition = posRect.anchoredPosition;
            }
        }
    }


    // 清空换位面板，清理临时对象
    public void ClearSwitchSeatPanel() {
        // 清除文本内容
        if (NextRoundName != null) NextRoundName.text = "";
        if (SelfName != null) SelfName.text = "";
        if (RightName != null) RightName.text = "";
        if (TopName != null) TopName.text = "";
        if (LeftName != null) LeftName.text = "";

        // 重置面板位置
        ResetPanelPositions();

        // 隐藏面板
        gameObject.SetActive(false);
    }

}
