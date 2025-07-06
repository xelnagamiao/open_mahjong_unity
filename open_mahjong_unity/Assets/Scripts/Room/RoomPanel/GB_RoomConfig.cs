using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GB_RoomConfig : MonoBehaviour
{
    [SerializeField] private TMP_Text rule; // 规则
    [SerializeField] private TMP_Text round; // 圈数
    [SerializeField] private TMP_Text roundTime; // 局时
    [SerializeField] private TMP_Text stepTime; // 步时
    [SerializeField] private TMP_Text tips; // 提示
    [SerializeField] private TMP_Text password; // 密码

    public void SetGBRoomConfig(RoomInfo roomInfo)
    {
        rule.text = "国标麻将"; // 规则
        round.text = roomInfo.max_round.ToString(); // 圈数
        roundTime.text = roomInfo.round_timer.ToString(); // 局时
        stepTime.text = roomInfo.step_timer.ToString(); // 步时
        tips.text = roomInfo.tips ? "有提示" : "无提示"; // 提示
        password.text = roomInfo.has_password ? "有密码" : "无密码"; // 密码
    }
}
