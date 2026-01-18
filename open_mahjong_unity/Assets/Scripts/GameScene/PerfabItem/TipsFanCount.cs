using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
public class TipsFanCount : MonoBehaviour
{
    [SerializeField] private TMP_Text tipsFanCountText; // 提示番数文本

    public void SetTipsFanCount(string fanCount) {
        tipsFanCountText.text = fanCount;
    }
}
