using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ProfileOnClick : MonoBehaviour, IPointerClickHandler
{
    public int user_id;
    
    // 当物体被点击时调用
    public void OnPointerClick(PointerEventData eventData)
    {
        if (NetworkManager.Instance != null && user_id >= 10)
        {
            DataNetworkManager.Instance.GetGuobiaoStats(user_id.ToString());
        }
    }
}
