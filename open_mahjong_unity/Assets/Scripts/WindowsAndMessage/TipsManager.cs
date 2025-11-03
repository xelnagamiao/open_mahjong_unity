using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TipsManager : MonoBehaviour
{
    public static TipsManager Instance { get; private set; }
    
    [SerializeField] private GameObject tipsPrefab; // Tips预制体
    [SerializeField] private Transform tipsCanvas; // Tips父容器，如果不设置则使用Canvas
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.Log($"Destroying duplicate TipsManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 显示提示信息
    public void ShowTip(bool isSuccess, string context, float duration = 3f)
    {
        if (tipsPrefab == null)
        {
            Debug.LogError("TipsManager: tipsPrefab未设置！");
            return;
        }
        
        // 创建tips实例
        GameObject tipInstance = Instantiate(tipsPrefab, tipsCanvas);
        
        // 获取Text组件并设置内容
        TextMeshProUGUI tipText = tipInstance.GetComponentInChildren<TextMeshProUGUI>();
        if (tipText != null)
        {
            tipText.text = context;
            
            // 根据成功/失败设置颜色
            if (isSuccess)
            {
                tipText.color = Color.green; // 成功显示绿色
            }
            else
            {
                tipText.color = Color.red; // 失败显示红色
            }
        }
        else
        {
            Debug.LogWarning("TipsManager: tips预制体中未找到TextMeshProUGUI组件");
        }
        
        // 设置背景颜色（如果有Image组件）
        Image tipImage = tipInstance.GetComponent<Image>();
        if (tipImage != null)
        {
            if (isSuccess)
            {
                tipImage.color = new Color(0.2f, 0.8f, 0.2f, 0.8f); // 半透明绿色背景
            }
            else
            {
                tipImage.color = new Color(0.8f, 0.2f, 0.2f, 0.8f); // 半透明红色背景
            }
        }
        
        // 启动协程自动销毁tips
        StartCoroutine(DestroyTipAfterDelay(tipInstance, duration));
    }
    
    // tips销毁协程
    private IEnumerator DestroyTipAfterDelay(GameObject tipInstance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (tipInstance != null)
        {
            Destroy(tipInstance);
        }
    }
}