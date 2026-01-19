using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GB_RoomConfig : MonoBehaviour {
    [SerializeField] private TMP_Text rule; // 规则
    [SerializeField] private TMP_Text round; // 圈数
    [SerializeField] private TMP_Text roundTime; // 局时
    [SerializeField] private TMP_Text stepTime; // 步时
    [SerializeField] private TMP_Text fushiText; // 复式
    [SerializeField] private TMP_Text tipsText; // 提示
    [SerializeField] private TMP_Text cuoheText; // 错和
    [SerializeField] private TMP_Text password; // 密码

    // 单例模式
    public static GB_RoomConfig Instance { get; private set; }
    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的GB_RoomConfig实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    public void SetGBRoomConfig(RoomInfo roomInfo) {
        rule.text = "国标麻将"; // 规则
        round.text = GetMaxRoundText(roomInfo.game_round); // 圈数
        roundTime.text = roomInfo.round_timer.ToString(); // 局时
        stepTime.text = roomInfo.step_timer.ToString(); // 步时
        fushiText.text = roomInfo.random_seed == 0 ? "复式:关" : "复式:开"; // 复式
        tipsText.text = roomInfo.tips ? "提示:开" : "提示:关"; // 提示
        cuoheText.text = roomInfo.open_cuohe ? "错和:开" : "错和:关"; // 错和
        password.text = roomInfo.has_password ? "有" : "无"; // 密码
    }

    private string GetMaxRoundText(int game_round){
        if (game_round == 1) {
            return "东风战";
        } else if (game_round == 2) {
            return "东南战";
        } else if (game_round == 3) {
            return "东西战";
        } else if (game_round == 4) {
            return "全庄战";
        }
        return "设置game_round未知错误";
    }
}  
