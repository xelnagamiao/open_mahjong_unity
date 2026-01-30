using System;
using System.Collections.Generic;

/// <summary>
/// 专门负责解析牌谱 JSON，并把结果转换为 GameRecordManager 可用的数据结构。
/// GameRecordManager 只保存解析出的原始 action node 列表和巡目映射，不关心 JSON 细节。
/// </summary>
public static class GameRecordJsonDecoder {

    /// <summary>
    /// 一次解析所有 round_index_x 的数据。
    /// - 输出每局的 action node 列表（不包含 "Next"）。
    /// - 输出每局的「巡目索引 → 该巡目起始 action 索引」映射。
    /// </summary>
    public static void ParseAllRoundsFromJson(
        string recordJson,
        List<List<List<string>>> allRoundActionNodes,
        List<Dictionary<int, int>> allRoundXunmuToAction
    ){
        if (allRoundActionNodes == null) throw new ArgumentNullException(nameof(allRoundActionNodes));
        if (allRoundXunmuToAction == null) throw new ArgumentNullException(nameof(allRoundXunmuToAction));

        allRoundActionNodes.Clear();
        allRoundXunmuToAction.Clear();

        // round_index 从 1 开始，连续查找，直到找不到下一个 round_index_x 为止
        for (int roundIndex = 1; ; roundIndex++){
            string roundKey = $"\"round_index_{roundIndex}\"";
            int roundKeyIndex = recordJson.IndexOf(roundKey, StringComparison.Ordinal);
            if (roundKeyIndex < 0){
                break; // 没有更多 round 了
            }

            ParseSingleRoundInternal(
                recordJson,
                roundIndex,
                out var actionNodes,
                out var xunmuMap
            );

            allRoundActionNodes.Add(actionNodes);
            allRoundXunmuToAction.Add(xunmuMap);
        }
    }

    /// <summary>
    /// 解析单个 round_index_x：
    /// - 返回该局所有 action node（不含 "Next"），每条是一个字符串列表 ["cut","55","true"]。
    /// - 返回该局的巡目起始索引映射：xunmuIndex -> actionNodeStartIndex。
    /// </summary>
    private static void ParseSingleRoundInternal(
        string recordJson,
        int roundIndex,
        out List<List<string>> actionNodes,
        out Dictionary<int, int> xunmuToActionStartIndex
    ){
        actionNodes = new List<List<string>>();
        xunmuToActionStartIndex = new Dictionary<int, int>();

        string roundKey = $"\"round_index_{roundIndex}\"";
        int roundKeyIndex = recordJson.IndexOf(roundKey, StringComparison.Ordinal);
        if (roundKeyIndex < 0){
            throw new Exception($"在牌谱 JSON 中未找到 {roundKey} 节点");
        }

        // 从 roundKey 开始向后查找 "action_ticks"
        int actionTicksKeyIndex = recordJson.IndexOf("\"action_ticks\"", roundKeyIndex, StringComparison.Ordinal);
        if (actionTicksKeyIndex < 0){
            throw new Exception("在指定局的 JSON 片段中未找到 \"action_ticks\" 节点");
        }

        // 找到 "action_ticks": 之后的第一个 '[' 作为数组起点
        int arrayStart = recordJson.IndexOf('[', actionTicksKeyIndex);
        if (arrayStart < 0){
            throw new Exception("未找到 action_ticks 数组起始符 '['");
        }

        // 用一个简单的括号计数找到对应的 ']'
        int bracketCount = 0;
        int arrayEnd = -1;
        for (int i = arrayStart; i < recordJson.Length; i++){
            char c = recordJson[i];
            if (c == '[') bracketCount++;
            else if (c == ']'){
                bracketCount--;
                if (bracketCount == 0){
                    arrayEnd = i;
                    break;
                }
            }
        }

        if (arrayEnd < 0){
            throw new Exception("未能正确匹配 action_ticks 数组结束符 ']'");
        }

        string actionTicksArrayText = recordJson.Substring(arrayStart, arrayEnd - arrayStart + 1);

        // 顶层拆分每一条 item
        List<string> itemTexts = SplitTopLevelArrayItems(actionTicksArrayText);

        int currentXunmu = 0;
        bool hasActionInCurrentXunmu = false;

        int rawIndex = 0;
        foreach (string itemText in itemTexts){
            string trimmed = itemText.Trim();
            if (string.IsNullOrEmpty(trimmed)){
                rawIndex++;
                continue;
            }

            // 去掉外层中括号
            if (trimmed[0] == '[') trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("]")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

            // 按逗号切分（依赖于当前牌谱参数中字符串内容不会携带未转义的逗号）
            string[] parts = trimmed.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0){
                rawIndex++;
                continue;
            }

            // 第 0 个元素的动作名
            string actionType = ExtractFirstStringToken(parts[0]);
            if (string.IsNullOrEmpty(actionType)){
                rawIndex++;
                continue;
            }

            if (actionType == "Next"){
                // "Next" 只负责巡目切换，不进入 actionNodes
                if (hasActionInCurrentXunmu){
                    currentXunmu++;
                    hasActionInCurrentXunmu = false;
                } else {
                    // 本巡目前面没动作，直接进入下一巡目
                    currentXunmu++;
                }
                rawIndex++;
                continue;
            }

            // 把当前整条记录转换成字符串列表 ["cut","55","true"...]
            List<string> tokens = new List<string>();
            foreach (var p in parts){
                tokens.Add(p.Trim());
            }

            // 记录当前巡目的起始 action 索引
            if (!hasActionInCurrentXunmu){
                xunmuToActionStartIndex[currentXunmu] = actionNodes.Count;
                hasActionInCurrentXunmu = true;
            }

            actionNodes.Add(tokens);
            rawIndex++;
        }
    }

    /// <summary>
    /// 把形如 [ [...], [...], ... ] 的数组文本拆分成顶层每个 item 的文本。
    /// </summary>
    private static List<string> SplitTopLevelArrayItems(string arrayText){
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(arrayText)){
            return result;
        }

        int len = arrayText.Length;
        int i = 0;

        // 跳过开头的 '['
        while (i < len && char.IsWhiteSpace(arrayText[i])) i++;
        if (i < len && arrayText[i] == '[') i++;

        int itemStart = -1;
        int bracketDepth = 0;
        bool inString = false;
        char stringQuote = '\0';

        for (; i < len; i++){
            char c = arrayText[i];

            if (inString){
                if (c == stringQuote){
                    inString = false;
                }
                else if (c == '\\'){
                    if (i + 1 < len) i++;
                }
                continue;
            }

            if (c == '"' || c == '\''){
                inString = true;
                stringQuote = c;
                continue;
            }

            if (c == '['){
                if (bracketDepth == 0){
                    itemStart = i;
                }
                bracketDepth++;
            }
            else if (c == ']'){
                bracketDepth--;
                if (bracketDepth == 0 && itemStart >= 0){
                    int itemEnd = i;
                    string itemText = arrayText.Substring(itemStart, itemEnd - itemStart + 1);
                    result.Add(itemText);
                    itemStart = -1;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 从一个 token 中抽取第一个字符串（去掉包裹的引号等），仅用于识别动作名。
    /// 例如： "\"cut\"" → "cut"
    /// </summary>
    private static string ExtractFirstStringToken(string token){
        if (string.IsNullOrEmpty(token)){
            return null;
        }
        string t = token.Trim();
        if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2){
            t = t.Substring(1, t.Length - 2);
        }
        else if (t.StartsWith("'") && t.EndsWith("'") && t.Length >= 2){
            t = t.Substring(1, t.Length - 2);
        }
        return t;
    }
}