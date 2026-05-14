using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ActionBlock : MonoBehaviour, IPointerClickHandler {
    public string actionType;
    public int targetTile;
    // 立直麻将涉赤 5 时吃牌候选索引，-1 表示非吃牌或无候选（走默认 0）
    public int chiComboIndex = -1;

    // 当点击时调用
    public void OnPointerClick(PointerEventData eventData) {
        Debug.Log($"选择了行动 {actionType} chiComboIndex={chiComboIndex}");
        GameCanvas.Instance.ChooseAction(actionType, targetTile, chiComboIndex);
    }


    void Start() {
        
    }

    // Update is called once per frame
    void Update() {
        
    }


}
