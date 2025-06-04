using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ActionBlock : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] public string actionType; // 可以在Inspector中设置
    
    // 当点击时调用
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"点击了 {gameObject.name}");
        if (actionType!= ""){
            NetworkManager.Instance.SendAction(actionType);
        }
        else{
            Debug.Log("没有设置TipBlock的actionType");
        }
        // 点击后停止计时器
        GameSceneMannager.Instance.StopTimeRunning();
    }


    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


 
}
