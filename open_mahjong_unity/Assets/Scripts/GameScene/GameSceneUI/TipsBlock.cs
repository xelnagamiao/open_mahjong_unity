using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;

public class TipsBlock : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // showtipsblock代表提示棱形，TipsContainer代表提示容器
    public static TipsBlock Instance { get; private set; }

    public bool IsBlockActive => gameObject.activeSelf;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    public void ShowTipsBlock(List<int> selfHandTiles, List<string> combinationTiles){
        // 听牌提示由族自行计算的规则（如虹雀）：不能走通用 TingpaiCheck，
        // 这里若清空缓存会把刚算好的提示抹掉。
        if (RuleRegistry.Current != null && RuleRegistry.Current.TipsProvidedByGameState) return;
        selfHandTiles = new List<int>(selfHandTiles);
        var sourceHand = new List<int>(selfHandTiles);
        combinationTiles = new List<string>(combinationTiles);
        string subRule = GameSession.Current.SubRule;
        RuleManifest manifest = RuleRegistry.Current;
        int actualDraw = RuleRegistry.ActiveGameState?.GetCurrentHintDrawTile(null) ?? 0;
        int fullHandCount = manifest?.FullHandTileCount?.Invoke(subRule) ?? 14;
        bool currentDrawHand = actualDraw > 0
            && selfHandTiles.Count + combinationTiles.Count * 3 == fullHandCount;
        if (currentDrawHand && !selfHandTiles.Remove(actualDraw)) currentDrawHand = false;
        HashSet<int> waitingTiles = RuleTips.ComputeWaiting(manifest, new TingpaiQuery {
            OnReady = () => {
                if (this != null && GameSession.Current.SubRule == subRule && TableMirror.Current.SelfHandTiles != null
                    && TableMirror.Current.SelfHandTiles.OrderBy(t => t).SequenceEqual(sourceHand.OrderBy(t => t)))
                    ShowTipsBlock(sourceHand, combinationTiles);
            },
            SubRule = GameSession.Current.SubRule,
            Hand = selfHandTiles,
            Melds = combinationTiles,
            DetailedConfig = GameSession.Current.DetailedConfig,
            ExcludedSuit = RuleRegistry.ActiveGameState?.ExcludedSuit ?? 0,
        });
        int normalizedDraw = manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(actualDraw) : actualDraw;
        // 如果听牌列表不为空，则显示提示
        if (waitingTiles.Count > 0 && (!currentDrawHand || waitingTiles.Contains(normalizedDraw))){
            var waitingList = waitingTiles.ToList();
            gameObject.SetActive(true);
            TipsContainer.Instance.CacheTenpaiTips(selfHandTiles, waitingList);
            TipsContainer.Instance.SetTipsWithHand(selfHandTiles, waitingList);
        }
        else{
            gameObject.SetActive(false);
            TipsContainer.Instance.ClearTenpaiTipsCache();
        }
    }

    public void ShowRecordTips(RecordTipsContext ctx, List<int> handTiles, List<int> waitingTiles) {
        if (ctx == null || waitingTiles == null || waitingTiles.Count == 0) {
            HideTipsBlock();
            return;
        }
        gameObject.SetActive(true);
        TipsContainer.Instance.CacheTenpaiTips(handTiles, waitingTiles);
        TipsContainer.Instance.SetTipsWithRecordContext(ctx, handTiles, waitingTiles);
    }

    public void ShowProvidedTips() {
        gameObject.SetActive(true);
    }

    public void HideTipsBlock(){
        gameObject.SetActive(false);
        TipsContainer.Instance.ClearTenpaiTipsCache();
    }

    public void OnPointerEnter(PointerEventData eventData){
        Debug.Log("鼠标指向提示棱形");
        var recordMgr = GameRecordManager.Instance;
        if (recordMgr != null && recordMgr.gameObject.activeSelf && recordMgr.ShouldShowRecordTips()) {
            recordMgr.RefreshRecordTips();
            if (TipsContainer.Instance.HasCachedTenpaiTips) {
                TipsContainer.Instance.ShowTips();
            }
            return;
        }
        TipsContainer.Instance.ShowCachedTenpaiTipsFromBlock();
    }

    public void OnPointerExit(PointerEventData eventData){
        Debug.Log("鼠标离开提示棱形");
        // 鼠标离开时隐藏TipsContainer（内部会先清空内容）
        TipsContainer.Instance.HideTipsAfterPointerExit(eventData);
    }
}
