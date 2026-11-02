using TMPro;
using UnityEngine;

namespace Mahjong.SceneSettingsUI
{
    [DisallowMultipleComponent]
    public sealed class SceneSidebarAppearance : MonoBehaviour
    {
        [SerializeField] SceneSettingsSidebarStyle style;
        [SerializeField] GameObject panel;
        [SerializeField] UnityEngine.UI.Button[] layers,presets;
        [SerializeField] UnityEngine.UI.Image[] swatches;
        [SerializeField] UnityEngine.UI.Slider[] channels;
        [SerializeField] TMP_Text[] channelValues;
        [SerializeField] TMP_Text targetTitle,status;
        [SerializeField] TMP_InputField hex;
        [SerializeField] UnityEngine.UI.Image preview;
        [SerializeField] UnityEngine.UI.Button closeButton,applyButton,resetButton;
        int selectedLayer;
        bool syncing;
        Canvas popupCanvas,parentCanvas;
        public bool IsOpen=>panel&&panel.activeSelf;
        public int SelectedLayer=>selectedLayer;
        public UnityEngine.UI.Button[] Layers=>layers;
        public UnityEngine.UI.Button[] Presets=>presets;
        public UnityEngine.UI.Slider[] Channels=>channels;
        public TMP_InputField HexInput=>hex;
        public UnityEngine.UI.Button CloseButton=>closeButton;
        public UnityEngine.UI.Button ApplyButton=>applyButton;
        public UnityEngine.UI.Button ResetButton=>resetButton;
        public GameObject Panel=>panel;
        void OnEnable(){if(style)style.AppearanceChanged+=Refresh;Refresh();}
        void OnDisable(){if(style)style.AppearanceChanged-=Refresh;}
        void LateUpdate(){if(IsOpen)SyncCanvas();}
        void SyncCanvas()
        {
            if(!popupCanvas&&panel)popupCanvas=panel.GetComponent<Canvas>();
            if(!parentCanvas)parentCanvas=GetComponentInParent<Canvas>();
            if(!popupCanvas||!parentCanvas)return;
            var camera=parentCanvas.rootCanvas.worldCamera;
            if(popupCanvas.worldCamera!=camera)popupCanvas.worldCamera=camera;
        }
        public void Toggle(){panel.SetActive(!panel.activeSelf);if(panel.activeSelf)Refresh();else style.FlushPalette();}
        public void Close(){panel.SetActive(false);style.FlushPalette();}
        public void SelectLayer(int index){selectedLayer=Mathf.Clamp(index,0,SceneSettingsSidebarStyle.LayerNames.Length-1);status.text="";Refresh();}
        public void SetChannels(float ignored)
        {
            if(syncing)return;
            status.text="";
            style.SetLayerColor(selectedLayer,new Color(channels[0].value/255f,channels[1].value/255f,channels[2].value/255f,channels[3].value/100f));
        }
        public void ApplyHex()
        {
            if(!SceneConfigUi.TryParseHex(hex.text,out var color)){status.text="请输入 6 位或 8 位 HEX";return;}
            status.text="";style.SetLayerColor(selectedLayer,color);style.FlushPalette();
        }
        public void SubmitHex(string ignored)=>ApplyHex();
        public void ConfigureBaked(SceneSettingsSidebarStyle owner,GameObject popup,UnityEngine.UI.Button[] targetButtons,UnityEngine.UI.Image[] targetSwatches,UnityEngine.UI.Button[] presetButtons,UnityEngine.UI.Slider[] sliders,TMP_Text[] values,TMP_Text heading,TMP_Text message,TMP_InputField input,UnityEngine.UI.Image sample,UnityEngine.UI.Button close,UnityEngine.UI.Button apply,UnityEngine.UI.Button reset)
        {
            style=owner;panel=popup;layers=targetButtons;swatches=targetSwatches;presets=presetButtons;channels=sliders;channelValues=values;
            targetTitle=heading;status=message;hex=input;preview=sample;closeButton=close;applyButton=apply;resetButton=reset;
        }
        public void Refresh()
        {
            if(!style||!panel||!panel.activeSelf||layers==null)return;
            SyncCanvas();
            syncing=true;
            try
            {
                for(int i=0;i<layers.Length;i++)
                {
                    swatches[i].color=style.LayerColor(i);
                    layers[i].targetGraphic.color=i==selectedLayer?SceneConfigUi.TabOn:SceneConfigUi.TabOff;
                }
                for(int i=0;i<presets.Length;i++)presets[i].targetGraphic.color=style.IsPreset(i)?SceneConfigUi.TabOn:SceneConfigUi.TabOff;
                var color=style.LayerColor(selectedLayer);preview.color=color;targetTitle.text=SceneSettingsSidebarStyle.LayerNames[selectedLayer];
                channels[0].SetValueWithoutNotify(Mathf.Round(color.r*255));channels[1].SetValueWithoutNotify(Mathf.Round(color.g*255));
                channels[2].SetValueWithoutNotify(Mathf.Round(color.b*255));channels[3].SetValueWithoutNotify(Mathf.Round(color.a*100));
                for(int i=0;i<4;i++)channelValues[i].text=Mathf.RoundToInt(channels[i].value)+(i==3?"%":"");
                hex.SetTextWithoutNotify(ColorUtility.ToHtmlStringRGBA(color));
            }
            finally{syncing=false;}
        }
    }
}
