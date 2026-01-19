using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class GameSceneUIManager : MonoBehaviour
{
    public static GameSceneUIManager Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 清空所有临时面板
    /// </summary>
    public void ClearTemporaryPanels(){
        EndResultPanel.Instance.ClearEndResultPanel(); // 清空和牌结算面板
        EndGamePanel.Instance.ClearEndGamePanel();       // 清空游戏结束面板
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel(); // 清空换位面板
        EndLiujuPanel.Instance.ClearEndLiujuPanel();     // 清空流局面板
        StartGamePanel.Instance.ClearStartGamePanel();   // 清空开始游戏面板
        GameRecordManager.Instance.HideGameRecord();     // 隐藏游戏牌谱面板
        GameScoreRecord.Instance.Close();                 // 关闭分数记录面板
        GameCanvas.Instance.SetScoreRecordOpen(false);    // 隐藏计分板
        TipsBlock.Instance.HideTipsBlock(); // 隐藏提示面板
    }

    /// <summary>
    /// 显示和牌结算结果
    /// </summary>
    public void ShowEndResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask)
    {
        StartCoroutine(EndResultPanel.Instance.ShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask));
    }

    /// <summary>
    /// 显示流局面板
    /// </summary>
    public void ShowEndLiuju()
    {
        EndLiujuPanel.Instance.ShowLiujuPanel();
    }

    /// <summary>
    /// 显示换位面板
    /// </summary>
    public void ShowSwitchSeat(int current_round)
    {
        StartCoroutine(SwitchSeatPanel.Instance.ShowSwitchSeatPanel(current_round));
    }

    /// <summary>
    /// 显示游戏结束面板
    /// </summary>
    public void ShowEndGame(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data)
    {
        EndGamePanel.Instance.ShowGameEndPanel(game_random_seed, player_final_data);
    }

    /// <summary>
    /// 更新分数记录显示
    /// </summary>
    public void UpdateScoreRecord()
    {
        if (GameScoreRecord.Instance != null)
        {
            GameScoreRecord.Instance.UpdateScoreRecord();
        }
    }

}
