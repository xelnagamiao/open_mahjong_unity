using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatTextItem : MonoBehaviour
{
    private const float FADE_DURATION = 10f; // 渐隐持续时间（秒）
    private Coroutine fadeCoroutine; // 渐隐协程引用

    // Start is called before the first frame update
    void Start()
    {
        // 启动渐隐协程
        fadeCoroutine = StartCoroutine(FadeOutCoroutine());
    }

    // 渐隐协程
    private IEnumerator FadeOutCoroutine()
    {
        // 优先使用CanvasGroup控制透明度
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsedTime < FADE_DURATION)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / FADE_DURATION;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            yield return null;
        }

        // 确保最终透明度为0
        canvasGroup.alpha = 0f;
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

        // 直接设置透明度
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = alpha;
    }
}
