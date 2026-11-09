using TMPro;
using UnityEngine;

/// <summary>Serialized UI-only text binding; dynamic presenters keep writing their original source text.</summary>
[DefaultExecutionOrder(20000),DisallowMultipleComponent,RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedPlatformLabel : MonoBehaviour
{
    [TextArea] public string key;
    [HideInInspector] public string context;
    public bool followSourceChanges=true;
    public bool fitTranslation=true;
    public bool trimOuterWhitespace;
    public bool authoredAutoSize;
    [HideInInspector] public bool typographyBaked;
    public float authoredSize=24,minimumSize=14;
    TMP_Text label;
    string rendered;
    bool applied;
    bool extraSizingApplied, extraOriginalAuto;
    float extraOriginalSize, extraOriginalMin, extraOriginalMax;
    void OnEnable(){ConfigManager.OnLanguageChanged+=Refresh;Refresh();}
    void OnDisable(){ConfigManager.OnLanguageChanged-=Refresh;}
    void LateUpdate()
    {
        if(!label)return;
        if(followSourceChanges&&label.text!=rendered){key=label.text;Refresh();}
        else if((fitTranslation||PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage))&&ConfigManager.CurrentLanguage!=AppLanguage.SimplifiedChinese&&!label.enableAutoSizing)ApplyTypography();
    }
    public void Refresh()
    {
        if(!label)label=GetComponent<TMP_Text>();
        if(followSourceChanges&&((applied&&label.text!=rendered)||(!applied&&label.text!=key)))key=label.text;
        if(string.IsNullOrEmpty(key)&&!applied)key=label.text;
        rendered=Translate();label.text=rendered;applied=true;
        ApplyTypography();
    }
    void ApplyTypography()
    {
        if(!fitTranslation){
            bool extra=PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage);
            if(extra&&!extraSizingApplied){
                extraOriginalAuto=label.enableAutoSizing;
                extraOriginalSize=label.fontSize;extraOriginalMin=label.fontSizeMin;extraOriginalMax=label.fontSizeMax;
                extraSizingApplied=true;
            }
            if(extra&&!label.enableAutoSizing){
                label.fontSizeMax=label.fontSize;
                label.fontSizeMin=Mathf.Min(label.fontSize,Mathf.Min(minimumSize,14));
                label.enableAutoSizing=true;
            }else if(!extra&&extraSizingApplied){
                label.enableAutoSizing=extraOriginalAuto;
                label.fontSize=extraOriginalSize;label.fontSizeMin=extraOriginalMin;label.fontSizeMax=extraOriginalMax;
                extraSizingApplied=false;
            }
            return;
        }
        if(fitTranslation){
            bool translated=ConfigManager.CurrentLanguage!=AppLanguage.SimplifiedChinese;
            label.enableAutoSizing=translated||authoredAutoSize;
            float height=label.rectTransform.rect.height;
            float languageMinimum=translated?Mathf.Min(minimumSize,14):minimumSize;
            float fittingMinimum=height>0?Mathf.Min(languageMinimum,Mathf.Max(10,height*.65f)):languageMinimum;
            label.fontSizeMax=authoredSize;label.fontSizeMin=Mathf.Min(fittingMinimum,authoredSize);
            label.fontSize=authoredSize;
        }
    }
    public void SetSource(string source){
        key=source??"";
        if(!label)label=GetComponent<TMP_Text>();
        rendered=Translate();label.text=rendered;applied=true;
        ApplyTypography();
    }
    public string Source=>key;
    string Translate() { var value=PlatformTypography.NormalizeChineseMarkup(PlatformText.GetContextual(key,context)); return trimOuterWhitespace?value.Trim():value; }
}
