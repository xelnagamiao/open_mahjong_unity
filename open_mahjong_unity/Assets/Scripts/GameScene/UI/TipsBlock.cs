using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;

public class TipsBlock : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // showtipsblock代表提示棱形，TipsContainer代表提示容器
    public static TipsBlock Instance { get; private set; }
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    public void ShowTipsBlock(List<int> selfHandTiles, List<string> combinationTiles){
        HashSet<int> waitingTiles = new HashSet<int>();
        try {
            if (NormalGameStateManager.Instance.roomType == "guobiao"){
                waitingTiles = GBtingpai.TingpaiCheck(
                    selfHandTiles,
                    combinationTiles,
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomType == "qingque"){
                waitingTiles = Qingque13External.TingpaiCheck(selfHandTiles, combinationTiles, false);
            }
            else{
                Debug.LogWarning($"未知的规则类型: {NormalGameStateManager.Instance.roomType}");
                waitingTiles = new HashSet<int>();
            }
        } catch (System.Exception e) {
            Debug.LogError($"计算听牌列表时出错: {e.Message}");
        }
        // 如果听牌列表不为空，则显示提示
        if (waitingTiles.Count > 0){
            gameObject.SetActive(true);
            TipsContainer.Instance.SetTips(waitingTiles.ToList());
        }
        else{
            gameObject.SetActive(false);
        }
    }

    public void HideTipsBlock(){
        gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData){
        Debug.Log("鼠标指向提示棱形");
        // 鼠标指向时显示TipsContainer
        TipsContainer.Instance.ShowTips();

    }

    public void OnPointerExit(PointerEventData eventData){
        Debug.Log("鼠标离开提示棱形");
        // 鼠标离开时隐藏TipsContainer（内部会先清空内容）
        TipsContainer.Instance.HideTips();
    }
}
