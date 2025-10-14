using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas : MonoBehaviour
{
    // 显示倒计时
    public void LoadingRemianTime(int remainingTime, int cuttime){
        // 停止可能正在运行的倒计时协程
        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);
        
        // 保存初始时间值
        _currentRemainingTime = remainingTime;
        _currentCutTime = cuttime;
        
        // 设置倒计时初始值
        if (_currentCutTime > 0){
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
        }
        else{
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
        }

        // 启动倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    // 倒计时协程
    private IEnumerator CountdownTimer(){
        // 使用WaitForSeconds缓存，提高性能
        WaitForSeconds oneSecondWait = new WaitForSeconds(1.0f);
        
        while (_currentCutTime > 0 || _currentRemainingTime > 0){

            // 等待1秒
            yield return oneSecondWait;
            // 减少切牌时间
            if (_currentCutTime > 0){
                _currentCutTime--;
            }
            else if (_currentRemainingTime > 0){
                _currentRemainingTime--;
            }
            // 更新文本内容
            if (_currentCutTime > 0){
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
            }
            else{
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
            }
            // 决定文本颜色 低于5秒时显示红色
            if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
            {
                remianTimeText.color = Color.red;
            }
            else
            {
                remianTimeText.color = Color.white;
            }
            // 剩余时间为0 结束协程
            if (_currentRemainingTime == 0){
                remianTimeText.text = "";
                break;
            }
        }
    }

    public void StopTimeRunning(){

        // 停止倒计时
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        remianTimeText.text = $""; // 隐藏倒计时文本
        Debug.Log("停止倒计时,删除所有操作按钮");

        // 删除所有操作按钮
        foreach (Transform child in ActionBlockContenter){
            Destroy(child.gameObject);
        }
        foreach (Transform child in ActionButtonContainer){
            Destroy(child.gameObject);
        }
        ActionBlockContainerState = "None";

    }
}