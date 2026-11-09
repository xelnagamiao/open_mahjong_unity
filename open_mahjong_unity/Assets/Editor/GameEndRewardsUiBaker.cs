#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors final settlement and the shared Sala Coin presentation.</summary>
public static class GameEndRewardsUiBaker {
    public const string CoinPath = "Assets/Resources/Currency/sala_classic.png";

    public static void Bake(Scene scene) => FinalSettlementUiBaker.Bake(scene);

    public static void BakeShop(CompleteLobbyController owner) {
        var shop=owner.shop;
        CoinBefore(shop.balanceLabel,32);
        CoinBefore(shop.detailPrice,26);
        CoinBefore(shop.earnedLabel,20);
        CoinBefore(shop.rowTemplate.price,28);
        CoinBefore(shop.historyTemplate.amount,24);
        CoinBefore(shop.historyTemplate.balance,24);
        CoinBefore(owner.developerOptions.walletLabel,24);
        var rule=shop.transform.Find("ShopWindow/RewardRule")?.GetComponent<TMP_Text>();
        if(rule) {
            const string source=ShopPanel.RewardRuleText;
            var binding=rule.GetComponent<LocalizedPlatformLabel>();
            if(binding) binding.SetSource(source); else rule.text=source;
        }
    }

    public static void Validate(Scene scene) {
        var panel=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EndGamePanel>(true)).Single();
        var serialized=new SerializedObject(panel);
        if(!serialized.FindProperty("returnToTableButton").objectReferenceValue) throw new InvalidOperationException("牌谱结算返回牌桌按钮未烘焙。");
        var rows=serialized.FindProperty("rankDisplays");
        for(int i=0;i<rows.arraySize;i++) {
            var row=rows.GetArrayElementAtIndex(i);
            if(!Ref<TMP_Text>(row,"currency") || !Ref<Image>(row,"currencyIcon")?.sprite)
                throw new InvalidOperationException("全局结算货币引用缺失。");
        }
    }

    static void CoinBefore(TMP_Text text,float size) {
        var follow=text.GetComponent<CurrencyLabelIcon>();
        if(!follow) {
            follow=text.gameObject.AddComponent<CurrencyLabelIcon>();
            var rect=text.rectTransform;
            rect.sizeDelta-=new Vector2(size+6,0); rect.anchoredPosition+=new Vector2(size+6,0);
        }
        if(!follow.icon) follow.icon=Image("CurrencyIcon",text.transform);
        var image=follow.icon; image.sprite=Coin();image.color=Color.white;image.preserveAspect=true;image.raycastTarget=false;
        var icon=image.rectTransform;icon.anchorMin=icon.anchorMax=icon.pivot=new Vector2(.5f,.5f);icon.sizeDelta=new Vector2(size,size);
        icon.anchoredPosition=new Vector2(-text.rectTransform.rect.width*.5f-size*.5f-6,0);
        EditorUtility.SetDirty(text);EditorUtility.SetDirty(follow);
    }
    static Sprite Coin() => AssetDatabase.LoadAssetAtPath<Sprite>(CoinPath) ?? throw new InvalidOperationException("货币图标尚未导入为 Sprite。");
    static T Ref<T>(SerializedProperty row,string name) where T:UnityEngine.Object => row.FindPropertyRelative(name).objectReferenceValue as T;
    static Image Image(string name,Transform parent) {var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);return go.GetComponent<Image>();}
}
#endif
