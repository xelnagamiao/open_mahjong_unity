using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 立直麻将专用轮数面板。
/// 除常规规则/圈局外，展示：
/// - 本场棒数（honba）/ 场供立直棒数（riichi sticks）
/// - 宝牌指示槽位（固定 N 个，N 由 inspector 拖入的 StaticCard 数量决定；未翻开的位置显示牌背 0）
/// 承诺值 / 盐值由 RoundPanel 统管，点击 RoundPanel 区域后另行展示。
/// </summary>
public class RiichiRoundPanel : MonoBehaviour {
    [Header("房间信息显示")]
    [SerializeField] private TMP_Text ruleText;
    [SerializeField] private TMP_Text GameRoundText;
    [SerializeField] private TMP_Text roomNowRoundText;

    [Header("场况显示：数量")]
    [Tooltip("场供立直棒（1000点棒）数量")]
    [SerializeField] private TMP_Text riichiSticksCountText;
    [Tooltip("本场数量：三麻每本场200点，四麻300点；自摸每家另付100点")]
    [SerializeField] private TMP_Text honbaCountText;

    [Header("场况显示：宝牌指示槽位（手动拖入 StaticCard）")]
    [Tooltip("从左到右依序对应 1 张 dora 指示牌 + 最多 4 张杠宝牌。未翻开槽位固定显示牌背 0")]
    [SerializeField] private StaticCard[] doraSlots;

    private const int TileBackId = 0;
    private TMP_Text indicatorCaption, indicatorCounter;
    private UnityEngine.UI.HorizontalLayoutGroup indicatorRow;
    private TextAnchor originalIndicatorAlignment;
    private Vector2 originalIndicatorPosition;
    private Vector2 originalIndicatorAnchorMin, originalIndicatorAnchorMax;
    private bool originalIndicatorExpandWidth;
    private float originalIndicatorSpacing;
    private float[] originalIndicatorWidths;
    private Vector2 originalRoundSize;
    private RectTransform indicatorContent;
    private Vector2 originalContentPosition, originalContentSize;
    private TMP_Text seedHint;
    private Vector2 originalSeedPosition, originalSeedSize, originalSeedAnchorMin, originalSeedAnchorMax, originalSeedPivot;
    private float originalSeedFontSize;
    private readonly List<GameObject> counterDecorations = new List<GameObject>();
    private bool indicatorLayoutInitialized;

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        var manifest = RuleRegistry.Resolve(roomType, roomType)
            ?? RuleRegistry.Resolve(gameInfo.room_rule, gameInfo.sub_rule);
        if (ruleText != null) {
            ruleText.enableAutoSizing = true;
            ruleText.fontSizeMin = 20f;
            ruleText.fontSizeMax = 26f;
            ruleText.textWrappingMode = TextWrappingModes.NoWrap;
            ruleText.overflowMode = TextOverflowModes.Ellipsis;
            TableText.Set(ruleText, RuleNameDictionary.GetWholeName(roomType));
            ruleText.color = Color.black;
        }

        if (GameRoundText != null) {
            TableText.Set(GameRoundText, manifest?.MaxRoundInfoText?.Invoke(gameInfo)
                ?? RoundTextDictionary.GetMaxRoundText(roomType, gameInfo.max_round));
        }

        if (roomNowRoundText != null) {
            TableText.Set(roomNowRoundText, manifest?.RoundStatusText?.Invoke(gameInfo)
                ?? RoundTextDictionary.GetRoundName(roomType, gameInfo.current_round));
        }

        if (manifest?.RoundIndicators != null) {
            RefreshRuleIndicators(manifest.RoundIndicators(gameInfo));
            return;
        }
        SetIndicatorLayout(false);

        int honba = gameInfo.honba ?? 0;
        int sticks = gameInfo.riichi_sticks ?? 0;
        var indicators = gameInfo.dora_indicators != null
            ? new List<int>(gameInfo.dora_indicators)
            : new List<int>();
        if (gameInfo.kan_dora_indicators != null) {
            foreach (int t in gameInfo.kan_dora_indicators) indicators.Add(t);
        }
        RefreshFieldState(honba, sticks, indicators);
    }

    /// <summary>
    /// 更新场况显示。传入的 doraIndicators 已合并了初始宝牌与已翻开的杠宝牌。
    /// </summary>
    public void RefreshFieldState(int honba, int riichiSticks, List<int> doraIndicators) {
        SetIndicatorLayout(false);
        if (honbaCountText != null) honbaCountText.text = honba.ToString();
        if (riichiSticksCountText != null) riichiSticksCountText.text = riichiSticks.ToString();

        if (doraSlots == null) return;
        for (int i = 0; i < doraSlots.Length; i++) {
            if (doraSlots[i] == null) continue;
            doraSlots[i].gameObject.SetActive(true);
            int tileId = (doraIndicators != null && i < doraIndicators.Count) ? doraIndicators[i] : TileBackId;
            doraSlots[i].SetTileOnlyImage(tileId);
            TileFaceFit.FitRowHeight(doraSlots[i].transform.parent);
        }
    }

    /// <summary>在同一回合面板内显示翻出的牌，隐藏未使用的槽位与日麻点棒。</summary>
    public void RefreshRuleIndicators(RoundIndicatorDisplay display) {
        bool stacked = display?.CaptionAboveTiles == true;
        SetIndicatorLayout(true, stacked);
        if (indicatorCaption != null) TableText.Set(indicatorCaption, display?.Caption ?? "");
        if (indicatorCounter != null) TableText.Set(indicatorCounter, display?.Counter ?? "");
        if (doraSlots == null) return;
        for (int i = 0; i < doraSlots.Length; i++) {
            if (doraSlots[i] == null) continue;
            bool visible = display?.Tiles != null && i < display.Tiles.Length;
            doraSlots[i].gameObject.SetActive(visible);
            int tile = visible ? display.Tiles[i] : TileBackId;
            if(stacked) ((RectTransform)doraSlots[i].transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,46f/TileFaceFit.HandSizeFor(tile,1f).y);
            doraSlots[i].SetTileOnlyImage(tile);
        }
        if (doraSlots.Length > 0 && doraSlots[0] != null)
            TileFaceFit.FitRowHeight(doraSlots[0].transform.parent);
    }

    private void SetIndicatorLayout(bool custom, bool stacked = false) {
        if (!indicatorLayoutInitialized) {
            indicatorLayoutInitialized = true;
            if(roomNowRoundText!=null) originalRoundSize=roomNowRoundText.rectTransform.sizeDelta;
            indicatorContent=roomNowRoundText?.transform.parent as RectTransform;
            if(indicatorContent!=null) {
                originalContentPosition=indicatorContent.anchoredPosition;originalContentSize=indicatorContent.sizeDelta;
                seedHint=indicatorContent.Find("RandomSeed")?.GetComponent<TMP_Text>();
                if(seedHint!=null) {
                    var rect=seedHint.rectTransform;
                    originalSeedPosition=rect.anchoredPosition;originalSeedSize=rect.sizeDelta;
                    originalSeedAnchorMin=rect.anchorMin;originalSeedAnchorMax=rect.anchorMax;originalSeedPivot=rect.pivot;
                    originalSeedFontSize=seedHint.fontSize;
                }
            }
            if (doraSlots != null && doraSlots.Length > 0 && doraSlots[0] != null) {
                indicatorRow = doraSlots[0].transform.parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (indicatorRow != null) {
                    originalIndicatorAlignment = indicatorRow.childAlignment;
                    var rowRect = (RectTransform)indicatorRow.transform;
                    originalIndicatorPosition = rowRect.anchoredPosition;
                    originalIndicatorAnchorMin = rowRect.anchorMin;
                    originalIndicatorAnchorMax = rowRect.anchorMax;
                    originalIndicatorExpandWidth = indicatorRow.childForceExpandWidth;
                    originalIndicatorSpacing = indicatorRow.spacing;
                }
                originalIndicatorWidths = new float[doraSlots.Length];
                for(int i=0;i<doraSlots.Length;i++) if(doraSlots[i]!=null) originalIndicatorWidths[i]=((RectTransform)doraSlots[i].transform).rect.width;
            }
            if (honbaCountText != null) {
                foreach (Transform child in honbaCountText.transform.parent)
                    if (child.name == "100image" || child.name == "1000image" || child.name == "x")
                        counterDecorations.Add(child.gameObject);
            }
        }
        if (custom && indicatorCaption == null && honbaCountText != null) {
            indicatorCaption = CreateIndicatorLabel("IndicatorCaption", new Vector2(85, -8), new Vector2(185, 67));
            indicatorCounter = CreateIndicatorLabel("IndicatorCounter", new Vector2(125, -78), new Vector2(155, 30));
        }
        if (indicatorCaption != null) indicatorCaption.gameObject.SetActive(custom);
        if (indicatorCaption != null) {
            indicatorCaption.rectTransform.anchoredPosition = stacked ? new Vector2(15,-2) : new Vector2(85,-8);
            indicatorCaption.rectTransform.sizeDelta = stacked ? new Vector2(258,24) : new Vector2(185,67);
            indicatorCaption.fontSizeMax = stacked ? 20f : 24f;
        }
        if (indicatorCounter != null) indicatorCounter.gameObject.SetActive(custom);
        if (honbaCountText != null) honbaCountText.gameObject.SetActive(!custom);
        if (riichiSticksCountText != null) riichiSticksCountText.gameObject.SetActive(!custom);
        if(roomNowRoundText!=null) roomNowRoundText.rectTransform.sizeDelta=stacked?new Vector2(originalRoundSize.x,36f):originalRoundSize;
        if(indicatorContent!=null) {
            indicatorContent.anchoredPosition=originalContentPosition;indicatorContent.sizeDelta=originalContentSize;
            if(stacked) indicatorContent.offsetMin+=new Vector2(0,-32);
        }
        if(seedHint!=null) {
            var rect=seedHint.rectTransform;
            rect.anchorMin=stacked?new Vector2(0,1):originalSeedAnchorMin;
            rect.anchorMax=stacked?new Vector2(0,1):originalSeedAnchorMax;
            rect.pivot=stacked?new Vector2(0,1):originalSeedPivot;
            rect.anchoredPosition=stacked?new Vector2(15,-116):originalSeedPosition;
            rect.sizeDelta=stacked?new Vector2(258,18):originalSeedSize;
            seedHint.fontSize=stacked?12f:originalSeedFontSize;
        }
        foreach (var decoration in counterDecorations) decoration.SetActive(!custom);
        if (indicatorRow != null) {
            indicatorRow.childAlignment = custom ? TextAnchor.MiddleLeft : originalIndicatorAlignment;
            indicatorRow.childForceExpandWidth = stacked ? false : originalIndicatorExpandWidth;
            indicatorRow.spacing = stacked ? 8f : originalIndicatorSpacing;
            var rowRect = (RectTransform)indicatorRow.transform;
            rowRect.anchorMin = stacked ? new Vector2(0.5f, 1f) : originalIndicatorAnchorMin;
            rowRect.anchorMax = stacked ? new Vector2(0.5f, 1f) : originalIndicatorAnchorMax;
            rowRect.anchoredPosition = stacked ? new Vector2(originalIndicatorPosition.x, -51f) : originalIndicatorPosition;
        }
        if(!stacked && originalIndicatorWidths!=null)
            for(int i=0;i<doraSlots.Length;i++) if(doraSlots[i]!=null) ((RectTransform)doraSlots[i].transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,originalIndicatorWidths[i]);
    }

    private TMP_Text CreateIndicatorLabel(string name, Vector2 position, Vector2 size) {
        var label = Instantiate(honbaCountText, honbaCountText.transform.parent);
        label.name = name;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 24f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return label;
    }
}
