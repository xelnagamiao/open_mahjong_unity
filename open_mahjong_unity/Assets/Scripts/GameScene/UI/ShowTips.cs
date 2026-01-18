using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShowTips : MonoBehaviour
{
    private void Start()
    {
        TipsContainer.Instance.gameObject.SetActive(false);
    }
    private void OnMouseEnter()
    {
        // 鼠标指向时显示TipsContainer
        TipsContainer.Instance.gameObject.SetActive(true);
    }

    private void OnMouseExit()
    {
        // 鼠标离开时隐藏TipsContainer
        TipsContainer.Instance.gameObject.SetActive(false);
    }
}
