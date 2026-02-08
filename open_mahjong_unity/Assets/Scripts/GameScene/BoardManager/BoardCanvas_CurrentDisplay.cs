using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public partial class BoardCanvas {
    
    public void ShowCurrentPlayer(string currentPlayerIndex){
        // 1. 如果存在旧的闪烁动画，则停止它
        if (flashCoroutine != null){
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        // 2. 隐藏所有玩家的当前回合标识
        player_self_current_image.gameObject.SetActive(false);
        player_left_current_image.gameObject.SetActive(false);
        player_top_current_image.gameObject.SetActive(false);
        player_right_current_image.gameObject.SetActive(false);

        // 3. 显示当前玩家的标识
        Image targetImage = null;
        if (currentPlayerIndex == "self"){
            player_self_current_image.gameObject.SetActive(true);
            targetImage = player_self_current_image;
        } else if (currentPlayerIndex == "left"){
            player_left_current_image.gameObject.SetActive(true);
            targetImage = player_left_current_image;
        } else if (currentPlayerIndex == "top"){
            player_top_current_image.gameObject.SetActive(true);
            targetImage = player_top_current_image;
        } else if (currentPlayerIndex == "right"){
            player_right_current_image.gameObject.SetActive(true);
            targetImage = player_right_current_image;
        }

        // 设置初始透明度为100%
        Color color = targetImage.color;
        color.a = 1f;
        targetImage.color = color;

        // 4. 启动闪烁协程
        flashCoroutine = StartCoroutine(FlashImage(targetImage));

        // 5. 更新剩余牌数
        remiansTilesText.text = $"余:{NormalGameStateManager.Instance.remainTiles}";
    }

    private IEnumerator FlashImage(Image image) {
        float cycleDuration = 2.0f; // 每个明暗循环的周期（2秒）
        float elapsedTime = 0f;
        
        while (true) {
            // 闪烁效果
            float progress = (elapsedTime % cycleDuration) / cycleDuration; // 0到1的循环进度
            float pingPongValue = Mathf.PingPong(progress * 2f, 1f); // 0到1之间来回振荡
            float alpha = 1f - pingPongValue; // 取倒数：亮→暗→亮
            
            Color color = image.color;
            color.a = alpha;
            image.color = color;
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }
}