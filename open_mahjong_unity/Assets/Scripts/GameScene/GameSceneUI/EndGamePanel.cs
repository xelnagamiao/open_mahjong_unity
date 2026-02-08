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

    // ✅ 参数 key 为 string（用户ID），不排序，按字典当前顺序取前4
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);

        // 直接按字典的枚举顺序取前4个玩家（假设服务器已排好序）
        var enumerator = player_final_data.GetEnumerator();
        for (int i = 0; i < 4; i++) {
            if (i < rankDisplays.Length && enumerator.MoveNext()) {
                var playerData = enumerator.Current.Value; // 获取当前玩家数据
                var display = rankDisplays[i]; // 获取视窗列表中的第i个元素

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
    }

    public void ClearEndGamePanel(){
        gameObject.SetActive(false);
    }

}