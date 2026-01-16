using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TestGBhepai : MonoBehaviour
{
    [Header("UI 组件")]
    [Tooltip("输入框：输入 Python 风格的测试数据，例如：[[\"k39\"],[32,32,32,33,33,33,34,34,34,41,41],33,[\"点和\"]]")]
    public TMP_InputField testInputField;
    
    [Tooltip("运行测试按钮")]
    public Button testButton;
    
    [Tooltip("结果显示文本")]
    public TMP_Text resultText;

    private void Start(){
        testButton.onClick.AddListener(TestHepaiCheck);
    }

    [ContextMenu("Test Hepai Check (Parse Input)")]
    public void TestHepaiCheck()
    {
        string inputText = "";
        
        // 如果有输入框，使用输入框的内容
        if (testInputField != null && !string.IsNullOrWhiteSpace(testInputField.text))
        {
            inputText = testInputField.text.Trim();
        }
        else
        {
            // 否则使用默认测试数据
            inputText = "[[\"k39\"],[32,32,32,33,33,33,34,34,34,41,41],33,[\"点和\"]]";
            Debug.LogWarning("没有输入数据，使用默认测试数据");
        }

        try
        {
            var (combinationList, tilesList, hepaiTile, wayToHepai) = ParseTestData(inputText);
            
            Debug.Log($"解析结果:");
            Debug.Log($"  组合列表: [{string.Join(", ", combinationList)}]");
            Debug.Log($"  手牌列表: [{string.Join(", ", tilesList)}]");
            Debug.Log($"  和牌张: {hepaiTile}");
            Debug.Log($"  和牌方式: [{string.Join(", ", wayToHepai)}]");

            // 调用 GBhepai 的静态方法进行和牌检查
            var result = GBhepai.HepaiCheck(tilesList, combinationList, wayToHepai, hepaiTile, debug: true);

            // 测试脚本负责显示结果在 TMP Text 上
            string resultMessage = $"最终结果:\n得分: {result.Item1}\n番种: {string.Join(", ", result.Item2)}";
            
            if (resultText != null)
            {
                resultText.text = resultMessage;
            }
            else
            {
                Debug.Log(resultMessage);
            }
        }
        catch (Exception e)
        {
            string errorMessage = $"解析或测试失败:\n{e.Message}\n\n{e.StackTrace}";
            
            if (resultText != null)
            {
                resultText.text = errorMessage;
            }
            else
            {
                Debug.LogError(errorMessage);
            }
        }
    }

    /// <summary>
    /// 解析 Python 风格的测试数据字符串
    /// 格式: [[组合列表], [手牌列表], 和牌张, [和牌方式列表]]
    /// 例如: [[\"k39\"],[32,32,32,33,33,33,34,34,34,41,41],33,[\"点和\"]]
    /// </summary>
    private (List<string> combinationList, List<int> tilesList, int hepaiTile, List<string> wayToHepai) ParseTestData(string input)
    {
        // 移除首尾空白字符
        input = input.Trim();
        
        // 移除首尾的方括号
        if (input.StartsWith("[") && input.EndsWith("]"))
        {
            input = input.Substring(1, input.Length - 2).Trim();
        }

        // 使用正则表达式或手动解析来提取4个元素
        var elements = new List<string>();
        int bracketDepth = 0;
        int startIndex = 0;
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            
            // 处理字符串
            if ((c == '"' || c == '\'') && (i == 0 || input[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    inString = false;
                }
                continue;
            }

            if (!inString)
            {
                if (c == '[')
                    bracketDepth++;
                else if (c == ']')
                    bracketDepth--;
                else if (c == ',' && bracketDepth == 0)
                {
                    elements.Add(input.Substring(startIndex, i - startIndex).Trim());
                    startIndex = i + 1;
                }
            }
        }
        
        // 添加最后一个元素
        if (startIndex < input.Length)
        {
            elements.Add(input.Substring(startIndex).Trim());
        }

        if (elements.Count != 4)
        {
            throw new ArgumentException($"期望4个元素，但得到 {elements.Count} 个: {string.Join(" | ", elements)}");
        }

        // 解析组合列表（第一个元素）
        List<string> combinationList = ParseStringList(elements[0]);
        
        // 解析手牌列表（第二个元素）
        List<int> tilesList = ParseIntList(elements[1]);
        
        // 解析和牌张（第三个元素）
        int hepaiTile = int.Parse(elements[2].Trim());
        
        // 解析和牌方式列表（第四个元素）
        List<string> wayToHepai = ParseStringList(elements[3]);

        return (combinationList, tilesList, hepaiTile, wayToHepai);
    }

    /// <summary>
    /// 解析字符串列表，例如: [\"k39\",\"k14\"] 或 []
    /// </summary>
    private List<string> ParseStringList(string listStr)
    {
        listStr = listStr.Trim();
        
        // 空列表
        if (listStr == "[]")
            return new List<string>();

        // 移除首尾的方括号
        if (listStr.StartsWith("[") && listStr.EndsWith("]"))
        {
            listStr = listStr.Substring(1, listStr.Length - 2).Trim();
        }

        if (string.IsNullOrWhiteSpace(listStr))
            return new List<string>();

        var result = new List<string>();
        var matches = Regex.Matches(listStr, @"[""']([^""']+)[""']");
        
        foreach (Match match in matches)
        {
            result.Add(match.Groups[1].Value);
        }

        return result;
    }

    /// <summary>
    /// 解析整数列表，例如: [32,32,32,33,33,33] 或 []
    /// </summary>
    private List<int> ParseIntList(string listStr)
    {
        listStr = listStr.Trim();
        
        // 空列表
        if (listStr == "[]")
            return new List<int>();

        // 移除首尾的方括号
        if (listStr.StartsWith("[") && listStr.EndsWith("]"))
        {
            listStr = listStr.Substring(1, listStr.Length - 2).Trim();
        }

        if (string.IsNullOrWhiteSpace(listStr))
            return new List<int>();

        var result = new List<int>();
        var parts = listStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out int value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
