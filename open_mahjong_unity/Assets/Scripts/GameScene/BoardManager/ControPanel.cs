using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ControPanel : MonoBehaviour, IPointerClickHandler {
    [SerializeField] private bool forwardClickToMouseController = true;
    // Start is called before the first frame update
    void Start() {
        
    }

    // Update is called once per frame
    void Update() {
        
    }

    // 鼠标点击事件处理
    public void OnPointerClick(PointerEventData eventData) {
        Debug.Log("点击了contropanel");
        // 调用 BoardCanvas 显示分差，如果协程正在运行会重置时间
        if (BoardCanvas.Instance != null) {
            BoardCanvas.Instance.ShowScoreDifference();
        }

        // 转发到牌谱鼠标控制器：让世界空间面板点击与底层鼠标逻辑同时生效
        if (forwardClickToMouseController && GameSceneMouseInputController.Instance != null) {
            GameSceneMouseInputController.Instance.HandleExternalPointerClick(eventData);
        }
    }
}
