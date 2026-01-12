using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PosPanel3D : MonoBehaviour {
    [Header("手牌位置")]
    [SerializeField] public Transform cardsPosition; // 手牌位置
    
    [Header("弃牌位置")]
    [SerializeField] public Transform discardsPosition; // 弃牌位置
    
    [Header("补花位置")]
    [SerializeField] public Transform buhuaPosition; // 补花位置
    
    [Header("组合位置")]
    [SerializeField] public Transform combinationsPosition; // 组合位置
    
    [Header("组合牌3D对象数组")]
    [SerializeField] public Transform[] combination3DObjects = new Transform[4]; // 组合牌3D对象数组
}
