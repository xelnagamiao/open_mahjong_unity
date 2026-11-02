using UnityEngine;
using TMPro;

namespace Salasasa.Events
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class EventVenueBinding : MonoBehaviour
    {
        public string key;
        public int index;
        public TMP_Text label;
        public bool semanticColors;
        public Color normalInk, positiveInk, pendingInk, negativeInk, neutralInk;
        float authoredSize;
        bool authoredBestFit;
        public void SetText(string value) {
            if(!label)return;
            if(authoredSize==0){authoredSize=label.fontSize;authoredBestFit=label.enableAutoSizing;}
            label.text=value;
            if(semanticColors)label.color=ReadColor(value);
            if(label.rectTransform.rect.height<=100){label.enableAutoSizing=authoredBestFit||ConfigManager.CurrentLanguage!=AppLanguage.SimplifiedChinese;label.fontSizeMax=authoredSize;label.fontSizeMin=Mathf.Max(12,authoredSize*.7f);}
        }
        Color ReadColor(string value)
        {
            if(key=="start"||key=="end")return value==PlatformText.Get("未定")?neutralInk:positiveInk;
            if(key=="waiting")return value=="—"?neutralInk:positiveInk;
            foreach(var text in new[]{"进行中","已开启","已通过","已报名","已加入","已选择"})
                if(value==PlatformText.Get(text))return positiveInk;
            foreach(var text in new[]{"待审核","已注册","重开审核中","未开赛"})
                if(value==PlatformText.Get(text))return pendingInk;
            foreach(var text in new[]{"已拒绝","已关闭","已取消"})
                if(value==PlatformText.Get(text))return negativeInk;
            return normalInk;
        }
    }
}
