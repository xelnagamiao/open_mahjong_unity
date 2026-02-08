using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ActionBlock : MonoBehaviour, IPointerClickHandler {
    public string actionType;
    public int targetTile;
    
    // 当点击时调用
    public void OnPointerClick(PointerEventData eventData) {
        Debug.Log($"选择了行动 {actionType}");
        GameCanvas.Instance.ChooseAction(actionType,targetTile);
    }


    void Start() {
        
    }

    // Update is called once per frame
    void Update() {
        
    }


}
