using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatTextItem : MonoBehaviour
{
    private const float FADE_DURATION = 5; // 渐隐持续时间（秒）
    private Coroutine fadeCoroutine; // 渐隐协程引用
    private CanvasGroup canvasGroup; // CanvasGroup 引用

    // Awake 在对象创建时立即调用
    void Awake()
    {
        // 获取或创建 CanvasGroup
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        // 启动渐隐协程
        fadeCoroutine = StartCoroutine(FadeOutCoroutine());
    }

    // 渐隐协程
    private IEnumerator FadeOutCoroutine()
    {
        // 确保 CanvasGroup 存在
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsedTime < FADE_DURATION)
        {
            // 检查透明度是否已被外部设置为0（比如通过 SetChildrenAlpha）
            if (canvasGroup != null && canvasGroup.alpha <= 0f)
            {
                // 透明度已经为0，自然终止协程
                yield break;
            }

            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / FADE_DURATION;
            
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            }
            
            yield return null;
        }

        // 确保最终透明度为0
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    // 停止渐隐并设置透明度（供外部调用）
    public void SetAlpha(float alpha)
    {
        // 停止渐隐协程
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        // 确保 CanvasGroup 存在
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
        
        // 直接设置透明度
        canvasGroup.alpha = alpha;
    }
}
