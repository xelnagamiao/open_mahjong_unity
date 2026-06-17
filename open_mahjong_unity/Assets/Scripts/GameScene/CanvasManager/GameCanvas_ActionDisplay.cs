// GameCanvas 操作文本

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class GameCanvas{
    private const float GangScoreFloatDelaySeconds = 0.3f;
    private const float GangScoreFloatHoldSeconds = 1f;
    private const float GangScoreFloatFadeSeconds = 0.5f;
    private const float GangScoreFloatYOffset = -42f;

    // 显示操作文本
    public void ShowActionDisplay(string playerPosition, string actionType, string roomRule = null) {
        Transform displayPos = null;
        if (playerPosition == "self"){
            displayPos = SelfActionDisplayPos;
        } else if (playerPosition == "left"){
            displayPos = LeftActionDisplayPos;
        } else if (playerPosition == "right"){
            displayPos = RightActionDisplayPos;
        } else if (playerPosition == "top"){
            displayPos = TopActionDisplayPos;
        }
        if (displayPos == null) return;
        
        string displayText = GetActionDisplayText(actionType, roomRule);
        if (string.IsNullOrEmpty(displayText)) return;

        // 同一座位上若已有操作文本（含未走完渐隐协程的残留），先清掉再显示，避免叠字/卡死
        ClearActionDisplayAt(displayPos);

        // 实例化操作显示文本
        GameObject actionTextObj = Instantiate(ActionDisplayText, displayPos);
        
        // 设置文本内容
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        actionText.text = displayText;
        
        // 启动渐变消失协程
        StartCoroutine(FadeOutActionDisplay(actionTextObj,displayPos));
    }

    /// <summary>四川刮风下雨：各座位 ActionDisplayPos 飘字 +/- 分（默认杠字后 0.3s）。</summary>
    public void ShowGangScoreFloats(Dictionary<int, int> changesBySeat, float delaySeconds = GangScoreFloatDelaySeconds) {
        if (changesBySeat == null || !HasNonZeroGangScoreChanges(changesBySeat)) return;
        StartCoroutine(CoGangScoreFloats(changesBySeat, delaySeconds));
    }

    public static bool HasNonZeroGangScoreChanges(Dictionary<int, int> changes) {
        if (changes == null) return false;
        foreach (var kv in changes) {
            if (kv.Value != 0) return true;
        }
        return false;
    }

    private IEnumerator CoGangScoreFloats(Dictionary<int, int> changesBySeat, float delaySeconds) {
        if (delaySeconds > 0f) {
            yield return new WaitForSeconds(delaySeconds);
        }

        var gsm = NormalGameStateManager.Instance;
        if (gsm == null) yield break;

        var floatObjects = new List<GameObject>();
        foreach (var kvp in gsm.indexToPosition) {
            int seatIdx = kvp.Key;
            string pos = kvp.Value;
            if (!changesBySeat.TryGetValue(seatIdx, out int delta) || delta == 0) continue;

            Transform displayPos = GetActionDisplayPos(pos);
            if (displayPos == null) continue;

            GameObject floatObj = Instantiate(ActionDisplayText, displayPos);
            TMP_Text floatText = floatObj.GetComponent<TMP_Text>();
            if (floatText != null) {
                floatText.text = FormatGangScoreFloatText(delta);
                floatText.fontSize = Mathf.Max(20f, floatText.fontSize * 0.85f);
                RectTransform rt = floatObj.GetComponent<RectTransform>();
                if (rt != null) {
                    Vector2 anchored = rt.anchoredPosition;
                    rt.anchoredPosition = new Vector2(anchored.x, anchored.y + GangScoreFloatYOffset);
                }
            }
            floatObjects.Add(floatObj);
        }

        yield return new WaitForSeconds(GangScoreFloatHoldSeconds);

        float elapsed = 0f;
        var originalColors = new Dictionary<GameObject, Color>();
        foreach (GameObject obj in floatObjects) {
            if (obj == null) continue;
            TMP_Text t = obj.GetComponent<TMP_Text>();
            if (t != null) originalColors[obj] = t.color;
        }

        while (elapsed < GangScoreFloatFadeSeconds) {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / GangScoreFloatFadeSeconds);
            foreach (var kv in originalColors) {
                if (kv.Key == null) continue;
                TMP_Text t = kv.Key.GetComponent<TMP_Text>();
                if (t == null) continue;
                Color c = kv.Value;
                t.color = new Color(c.r, c.g, c.b, alpha);
            }
            yield return null;
        }

        foreach (GameObject obj in floatObjects) {
            if (obj != null) Destroy(obj);
        }
    }

    private Transform GetActionDisplayPos(string playerPosition) {
        if (playerPosition == "self") return SelfActionDisplayPos;
        if (playerPosition == "left") return LeftActionDisplayPos;
        if (playerPosition == "right") return RightActionDisplayPos;
        if (playerPosition == "top") return TopActionDisplayPos;
        return null;
    }

    private static string FormatGangScoreFloatText(int delta) {
        if (delta > 0) return $"<color=#00ff00>+{delta}</color>";
        if (delta < 0) return $"<color=#ff0000>{delta}</color>";
        return string.Empty;
    }

    /// <summary>销毁指定座位锚点下所有操作文本实例（不依赖渐隐协程）。</summary>
    private void ClearActionDisplayAt(Transform displayPos) {
        if (displayPos == null) return;
        for (int i = displayPos.childCount - 1; i >= 0; i--) {
            Transform child = displayPos.GetChild(i);
            if (child != null) {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 清空四个座位的全部操作文本（补花/碰/吃/杠/和等）。
    /// 退出对局/牌谱/观战、初始化与跳转回放时调用：FadeOutActionDisplay 协程依附于本 GameCanvas，
    /// 在 gameObject 被 SetActive(false) 时会被中断而不销毁文本实例，导致「补花」等字残留卡死，
    /// 这里直接销毁实例兜底。
    /// </summary>
    public void ClearActionDisplay() {
        ClearActionDisplayAt(SelfActionDisplayPos);
        ClearActionDisplayAt(LeftActionDisplayPos);
        ClearActionDisplayAt(RightActionDisplayPos);
        ClearActionDisplayAt(TopActionDisplayPos);
    }

    private string GetHuSelfActionText(string roomRule = null) {
        string rule = !string.IsNullOrEmpty(roomRule) ? roomRule : NormalGameStateManager.Instance?.roomRule;
        string subRule = NormalGameStateManager.Instance?.subRule;
        if (rule == "guobiao" || (subRule != null && subRule.StartsWith("guobiao/"))) {
            return "和";
        }
        return "自摸";
    }

    private string GetActionDisplayText(string actionType, string roomRule) {
        if (actionType == "chi_left" || actionType == "chi_mid" || actionType == "chi_right"){
            return "吃";
        } else if (actionType == "peng"){
            return "碰";
        } else if (actionType == "angang" || actionType == "jiagang" || actionType == "gang"){
            return "杠";
        } else if (actionType == "hu" || actionType == "hu_self" || actionType == "hu_first" || actionType == "hu_second" || actionType == "hu_third"){
            string rule = !string.IsNullOrEmpty(roomRule) ? roomRule : NormalGameStateManager.Instance.roomRule;
            if (actionType == "hu_self"){
                return GetHuSelfActionText(roomRule);
            }
            if (rule == "riichi") return "荣";
            return "和";
        } else if (actionType == "buhua"){
            return "补花";
        } else if (actionType == "riichi"){
            return "立直";
        }
        return string.Empty;
    }

    /// <summary>
    /// 观战用：显示和正常对局一致的操作按钮，但全部禁用（点击无效）。
    /// </summary>
    public void ShowSpectatorActionButtons(List<string> actionList) {
        ClearActionButton();
        if (actionList == null || actionList.Count == 0) {
            return;
        }

        SetActionButton(actionList);

        // 观战只展示按钮：移除点击回调（外观保持正常，点击无反应）
        foreach (Transform child in ActionButtonContainer) {
            Button btn = child.GetComponent<Button>();
            if (btn != null) {
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
            }
        }
    }

    // 渐变消失协程
    private IEnumerator FadeOutActionDisplay(GameObject actionTextObj,Transform displayPos) {
        if (actionTextObj == null) {
            Debug.LogWarning("actionTextObj is null");
            yield break;
        }
        
        TMP_Text actionText = actionTextObj.GetComponent<TMP_Text>();
        if (actionText == null) {
            Debug.LogWarning("actionText is null");
            yield break;
        }
        
        // 等待1秒
        yield return new WaitForSeconds(1f);
        
        // 渐变消失效果
        float fadeTime = 0.5f; // 渐变时间
        float elapsedTime = 0f;
        Color originalColor = actionText.color;
        
        while (elapsedTime < fadeTime) {
            if (actionTextObj == null) yield break;
            
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeTime);
            actionText.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            yield return null;
        }
        
        // 销毁对象
        if (actionTextObj != null) {
            Destroy(actionTextObj);
        }
    }
}
