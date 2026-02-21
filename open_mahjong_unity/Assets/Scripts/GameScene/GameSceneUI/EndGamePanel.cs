using System.Collections.Generic;
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

    // ✅ 参数 key 为 string（排名rank），按照rank顺序（1,2,3,4）显示
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);

        // 按照rank顺序（1,2,3,4）获取和显示玩家数据
        for (int rank = 1; rank <= 4; rank++) {
            string rankKey = rank.ToString();
            if (player_final_data.ContainsKey(rankKey) && (rank - 1) < rankDisplays.Length) {
                var playerData = player_final_data[rankKey]; // 获取当前排名玩家数据
                var display = rankDisplays[rank - 1]; // 获取视窗列表中的第(rank-1)个元素

                display.username.text = playerData["username"].ToString();
                display.score.text = playerData["score"].ToString();
                display.rank.text = playerData["rank"].ToString();
                display.pt.text = playerData["pt"].ToString();
            }
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