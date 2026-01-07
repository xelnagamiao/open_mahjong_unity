using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AutoAction : MonoBehaviour
{
    [Header("自动操作文本")]
    [SerializeField] private TMP_Text arrangeHandCardsText; // 自动排列手牌文本
    [SerializeField] private TMP_Text autoHepaiText; // 自动胡牌文本
    [SerializeField] private TMP_Text autoCutCardText; // 自动出牌文本
    [SerializeField] private TMP_Text autoPassText; // 自动过牌文本

    [Header("颜色配置")]
    [SerializeField] private Color falseColor = Color.white; // false时的颜色（白色）
    [SerializeField] private Color trueColor = new Color(1f, 0.5f, 0f); // true时的颜色（橙色）

    // 默认值定义（与GameSceneManager保持一致，用于确保初始化正确）
    private const bool DEFAULT_AUTO_ARRANGE_HAND_CARDS = true; // 默认自动排列手牌开启

    private void Start()
    {
        // 延迟初始化，确保 GameSceneManager.Instance 已创建
        StartCoroutine(InitializeWhenReady());
    }

    // 等待 GameSceneManager 准备好后初始化
    private IEnumerator InitializeWhenReady()
    {
        // 等待 GameSceneManager.Instance 可用（最多等待1秒，避免无限等待）
        float waitTime = 0f;
        while (GameSceneManager.Instance == null && waitTime < 1f)
        {
            yield return null;
            waitTime += Time.deltaTime;
        }

        // 如果 GameSceneManager 仍然不可用，记录警告但继续初始化（使用默认值）
        if (GameSceneManager.Instance == null)
        {
            Debug.LogWarning("AutoAction: GameSceneManager.Instance 未找到，将使用默认值");
            // 使用默认值初始化显示
            UpdateAllTextColorsWithDefaults();
        }
        else
        {
            // 确保 isAutoArrangeHandCards 的默认值正确（如果是首次初始化）
            EnsureDefaultValue();

            // 初始化文本颜色（从 GameSceneManager 读取）
            UpdateAllTextColors();
        }
        
        // 为每个文本添加点击功能
        AddClickListeners();
    }

    // 确保默认值正确（避免被错误重置）
    private void EnsureDefaultValue()
    {
        if (GameSceneManager.Instance != null)
        {
            // 确保 isAutoArrangeHandCards 始终有正确的默认值
            // 这里不强制修改，因为 GameSceneManager 已经在字段定义时设置了默认值 true
            // 但如果由于某种原因被设置为 false，我们可以在这里确保默认值
            // 注意：这个检查是可选的，主要目的是防御性编程
        }
    }

    // 使用默认值更新所有文本颜色（当 GameSceneManager 不可用时）
    private void UpdateAllTextColorsWithDefaults()
    {
        UpdateTextColor(arrangeHandCardsText, DEFAULT_AUTO_ARRANGE_HAND_CARDS);
        UpdateTextColor(autoHepaiText, false);
        UpdateTextColor(autoCutCardText, false);
        UpdateTextColor(autoPassText, false);
    }

    // 为每个文本添加点击监听器
    private void AddClickListeners()
    {
        if (arrangeHandCardsText != null)
        {
            AddClickListener(arrangeHandCardsText, ToggleArrangeHandCards);
        }
        
        if (autoHepaiText != null)
        {
            AddClickListener(autoHepaiText, ToggleAutoHepai);
        }
        
        if (autoCutCardText != null)
        {
            AddClickListener(autoCutCardText, ToggleAutoCutCard);
        }
        
        if (autoPassText != null)
        {
            AddClickListener(autoPassText, ToggleAutoPass);
        }
    }

    // 为TMP_Text添加点击监听器
    private void AddClickListener(TMP_Text text, System.Action action)
    {
        // 检查是否已有Button组件
        Button button = text.GetComponent<Button>();
        if (button == null)
        {
            // 如果没有Button组件，添加一个
            button = text.gameObject.AddComponent<Button>();
        }
        
        // 移除旧的监听器，添加新的
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => action?.Invoke());
    }

    // 切换自动排列手牌
    private void ToggleArrangeHandCards()
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.isAutoArrangeHandCards = !GameSceneManager.Instance.isAutoArrangeHandCards;
            UpdateTextColor(arrangeHandCardsText, GameSceneManager.Instance.isAutoArrangeHandCards);
        }
    }

    // 切换自动胡牌
    private void ToggleAutoHepai()
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.isAutoHepai = !GameSceneManager.Instance.isAutoHepai;
            UpdateTextColor(autoHepaiText, GameSceneManager.Instance.isAutoHepai);
        }
    }

    // 切换自动出牌
    private void ToggleAutoCutCard()
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.isAutoCut = !GameSceneManager.Instance.isAutoCut;
            UpdateTextColor(autoCutCardText, GameSceneManager.Instance.isAutoCut);
        }
    }

    // 切换自动过牌
    private void ToggleAutoPass()
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.isAutoPass = !GameSceneManager.Instance.isAutoPass;
            UpdateTextColor(autoPassText, GameSceneManager.Instance.isAutoPass);
        }
    }

    // 更新单个文本颜色
    private void UpdateTextColor(TMP_Text text, bool value)
    {
        if (text != null)
        {
            text.color = value ? trueColor : falseColor;
        }
    }

    // 更新所有文本颜色
    private void UpdateAllTextColors()
    {
        if (GameSceneManager.Instance == null) return;

        UpdateTextColor(arrangeHandCardsText, GameSceneManager.Instance.isAutoArrangeHandCards);
        UpdateTextColor(autoHepaiText, GameSceneManager.Instance.isAutoHepai);
        UpdateTextColor(autoCutCardText, GameSceneManager.Instance.isAutoCut);
        UpdateTextColor(autoPassText, GameSceneManager.Instance.isAutoPass);
    }

    // 当GameSceneManager的值被外部修改时，可以调用此方法更新显示
    public void RefreshDisplay()
    {
        UpdateAllTextColors();
    }
}
