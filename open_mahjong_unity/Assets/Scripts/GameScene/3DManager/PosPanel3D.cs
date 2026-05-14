using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PosPanel3D : MonoBehaviour {
    [Header("手牌位置")]
    [SerializeField] public Transform cardsPosition; // 手牌位置

    [Header("自家出牌动画起点（可选，未赋值则用手牌容器位置）")]
    [SerializeField] public Transform outputPos;

    [Header("立直点棒落点（每位玩家各自的 1000 点棒终点）")]
    [SerializeField] public Transform tenbouPos;

    [Header("和牌时手牌展开动画（可选）")]
    [SerializeField] public Animator handRevealAnimator;
    [Tooltip("须与 Animator 窗口 Parameters 中 Trigger 参数名完全一致（默认 Expand）。")]
    [SerializeField] public string handRevealExpandTrigger = "Expand";

    [Header("展示手牌位置")]
    [SerializeField] public Transform ShowCardsPosition; // 展示手牌位置
    
    [Header("弃牌位置")]
    [SerializeField] public Transform discardsPosition; // 弃牌位置
    
    [Header("补花位置")]
    [SerializeField] public Transform buhuaPosition; // 补花位置
    
    [Header("组合位置")]
    [SerializeField] public Transform combinationsPosition; // 组合位置
    
    [Header("组合牌3D对象数组")]
    [SerializeField] public Transform[] combination3DObjects = new Transform[4]; // 组合牌3D对象数组
}
