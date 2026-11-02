using UnityEngine;
using UnityEngine.UI;

/// <summary>Legacy uGUI Text binding for the event and club management pages.</summary>
[DefaultExecutionOrder(20000),DisallowMultipleComponent,RequireComponent(typeof(Text))]
public sealed class LocalizedPlatformText : MonoBehaviour
{
    [TextArea] public string key;
    public int authoredSize=22,minimumSize=14;
    public bool fitTranslation=true,authoredBestFit;
    Text label;
    string rendered;
    bool applied;
    void OnEnable(){ConfigManager.OnLanguageChanged+=Refresh;Refresh();}
    void OnDisable(){ConfigManager.OnLanguageChanged-=Refresh;}
    void LateUpdate(){if(label&&label.text!=rendered){key=label.text;Refresh();}}
    public void Refresh()
    {
        if(!label)label=GetComponent<Text>();
        if((applied&&label.text!=rendered)||(!applied&&label.text!=key))key=label.text;
        rendered=PlatformText.Get(key);label.text=rendered;applied=true;
        if(fitTranslation){label.resizeTextForBestFit=authoredBestFit||ConfigManager.CurrentLanguage!=AppLanguage.SimplifiedChinese;label.resizeTextMinSize=minimumSize;label.resizeTextMaxSize=authoredSize;label.fontSize=authoredSize;}
    }
    public string Source=>key;
}
