using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class CreatePanel : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Dropdown chooseRule; // 规则选择下拉框
    [SerializeField] private Button createRule; // 创建规则按钮
    [SerializeField] private GameObject GB_RulePanel; // 国标规则面板
    [SerializeField] private GameObject RC_RulePanel; // 立直规则面板
    [SerializeField] private GameObject MMC_RulePanel; // 莫莫柴规则面板

    [Header("Panel Settings")]
    [SerializeField] private GameObject[] rulePanels; // 所有规则面板数组

    private void Start()
    {
        // 订阅dropdown值改变事件
        chooseRule.onValueChanged.AddListener(OnRuleDropdownChanged);
        // 初始化面板显示
        InitializePanels();
    }

    /// <summary>
    /// 初始化所有面板的显示状态
    /// </summary>
    private void InitializePanels(){
        // 隐藏所有规则面板
        GB_RulePanel.SetActive(false);
        RC_RulePanel.SetActive(false);
        MMC_RulePanel.SetActive(false);
        // 根据当前dropdown值显示对应面板
        OnRuleDropdownChanged(chooseRule.value);
    }

    /// <summary>
    /// 处理规则选择下拉框值改变事件
    /// </summary>
    /// <param name="selectedIndex">选中的索引</param>
    private void OnRuleDropdownChanged(int selectedIndex){
        Debug.Log($"规则选择改变: {selectedIndex} - {chooseRule.options[selectedIndex].text}");
        // 隐藏所有规则面板
        GB_RulePanel.SetActive(false);
        RC_RulePanel.SetActive(false);
        MMC_RulePanel.SetActive(false);
        // 根据选择的索引显示对应面板
        switch (selectedIndex)
        {
            case 0: // 国标规则
                GB_RulePanel.SetActive(true);
                Debug.Log("显示国标规则面板");
                break;
                
            case 1: // 日式规则
                RC_RulePanel.SetActive(true);
                Debug.Log("显示日式规则面板");
                break;
                
            case 2: // 血战规则
                MMC_RulePanel.SetActive(true);
                Debug.Log("显示血战规则面板");
                break;
            
        }
    }
}
