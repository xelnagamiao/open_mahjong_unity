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
        
        // 更新UI显示
        if (_currentCutTime > 0){
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
        }
        else{
            remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
        }
        
        // 根据剩余时间改变文本颜色
        if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
        {
            remianTimeText.color = Color.red; // 时间不多时显示红色
        }
        else
        {
            remianTimeText.color = Color.white; // 正常时间显示白色
        }
        if (_currentRemainingTime == 0){
            remianTimeText.text = $""; // 如果剩余时间为0，则不显示剩余时间
            StopTimeRunning();
        }
        
        // 启动新的倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    // 倒计时协程
    private IEnumerator CountdownTimer(){
        // 使用WaitForSeconds缓存，提高性能
        WaitForSeconds oneSecondWait = new WaitForSeconds(1.0f);
        WaitForSeconds flashWait = new WaitForSeconds(0.05f);
        
        while (_currentCutTime > 0 || _currentRemainingTime > 0)
        {
            // 等待1秒
            yield return oneSecondWait;
            
            // 先减少切牌时间
            if (_currentCutTime > 0){
                _currentCutTime--;
            }
            else if (_currentRemainingTime > 0){
                _currentRemainingTime--;
            }
            
            // 更新UI显示
            if (_currentCutTime > 0){
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}+{_currentCutTime}";
            }
            else{
                remianTimeText.text = $"剩余时间: {_currentRemainingTime}";
            }
            
            // 根据剩余时间改变文本颜色
            if (_currentRemainingTime <= 5 && _currentCutTime <= 0)
            {
                remianTimeText.color = Color.red; // 时间不多时显示红色
                
                // 执行闪烁效果（使用安全的循环方式）
                yield return StartCoroutine(FlashWarningEffect());
            }
            else
            {
                remianTimeText.color = Color.white; // 正常时间显示白色
            }
            
            if (_currentRemainingTime == 0){
                remianTimeText.text = $""; // 如果剩余时间为0，则不显示剩余时间
                break; // 直接退出循环
            }
        }
        Debug.Log("倒计时结束！");
    }

    // 分离闪烁效果到独立的协程，避免死循环
    private IEnumerator FlashWarningEffect()
    {
        CanvasGroup canvasGroup = remianTimeText.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = remianTimeText.gameObject.AddComponent<CanvasGroup>();
        
        // 闪烁3次，使用整数循环避免浮点数精度问题
        for (int flashCount = 0; flashCount < 3; flashCount++)
        {
            // 渐隐：使用整数步数，避免浮点数精度问题
            for (int step = 0; step <= 7; step++) // 7步从1.0到0.3
            {
                float alpha = 1.0f - (step * 0.1f);
                canvasGroup.alpha = alpha;
                yield return new WaitForSeconds(0.05f);
            }
            
            // 渐显：使用整数步数，避免浮点数精度问题
            for (int step = 0; step <= 7; step++) // 7步从0.3到1.0
            {
                float alpha = 0.3f + (step * 0.1f);
                canvasGroup.alpha = alpha;
                yield return new WaitForSeconds(0.05f);
            }
        }
        
        // 确保最终透明度为1
        canvasGroup.alpha = 1f;
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