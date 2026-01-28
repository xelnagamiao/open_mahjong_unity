using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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

    public void ShowTipsBlock(){
        gameObject.SetActive(true);
        
    }

    public void HideTipsBlock(){
        gameObject.SetActive(false);
        TipsContainer.Instance.HideTipsTemp();
    }

    public void OnPointerEnter(PointerEventData eventData){
        Debug.Log("鼠标指向提示棱形");
        // 鼠标指向时显示TipsContainer
        if (TipsContainer.Instance.hasTips == true){
            TipsContainer.Instance.ShowTips();
        }
    }

    public void OnPointerExit(PointerEventData eventData){
        Debug.Log("鼠标离开提示棱形");
        // 鼠标离开时隐藏TipsContainer（内部会先清空内容）
        TipsContainer.Instance.HideTipsTemp();
    }
}
