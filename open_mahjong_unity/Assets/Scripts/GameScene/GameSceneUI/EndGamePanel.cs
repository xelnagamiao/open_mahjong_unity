using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EndGamePanel : MonoBehaviour {
    [SerializeField] private RankDisplay[] rankDisplays;

    [System.Serializable]
    public class RankDisplay {
        public TextMeshProUGUI username;
        public TextMeshProUGUI score;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI pt;
    }

    [SerializeField] private TextMeshProUGUI gameRandomSeed;
    [SerializeField] private Button goHomeButton;

    public static EndGamePanel Instance { get; private set; }

    private void Awake() {
        if (Instance == null){
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 参数 key 为 username，按照 rank 值升序（1,2,3,4）排列后显示。
    /// </summary>
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);

        // 按 rank 值升序排列所有玩家数据
        var sorted = player_final_data.Values
            .OrderBy(d => System.Convert.ToInt32(d["rank"]))
            .ToList();

        for (int i = 0; i < sorted.Count && i < rankDisplays.Length; i++) {
            var playerData = sorted[i];
            var display = rankDisplays[i];
            display.username.text = playerData["username"].ToString();
            display.score.text = playerData["score"].ToString();
            display.rank.text = playerData["rank"].ToString();
            display.pt.text = playerData["pt"].ToString();
        }
        // 显示游戏随机种子
        gameRandomSeed.text = "游戏随机种子: " + game_random_seed.ToString();
        // 设置主页按钮点击事件
        goHomeButton.onClick.RemoveAllListeners();
        goHomeButton.onClick.AddListener(OnGoHomeButtonClick);
    }

    private void OnGoHomeButtonClick() {
        gameObject.SetActive(false);
        WindowsManager.Instance.SwitchWindow("menu");
        Game3DManager.Instance.Clear3DTile(); // 清空3D手牌
    }

    public void ClearEndGamePanel(){
        gameObject.SetActive(false);
    }

}