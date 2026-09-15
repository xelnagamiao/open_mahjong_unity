using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    public sealed class SceneSettingsSidebarRow : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
    {
        public SceneSettingsSidebarBand band;
        public SceneSettingsSidebarIcon icon;
        public TMP_Text label;
        public Image marker;
        public bool selected, open=true;
        bool hover,down,focused;
        float value;
        void Update() => Draw(false);
        public void Draw(bool immediate)
        {
            float target=selected?(open?1:.25f):((hover||focused) ? .14f : 0f);
            if(down)target=Mathf.Max(0,target-.12f);
            value=immediate?target:Mathf.Lerp(value,target,1-Mathf.Exp(-Time.unscaledDeltaTime*18));
            if(band!=null && !Mathf.Approximately(band.emphasis,value)){band.emphasis=value;band.SetVerticesDirty();}
            // Hover and remembered-but-closed selections retain dark text on the pale fill.
            float ink=Mathf.InverseLerp(.45f,1,value);
            bool reset=band!=null && band.resetAction;
            if(label!=null) label.color=Color.Lerp(SceneSettingsSidebarBand.Hex(reset?"874D48":"43516B"),Color.white,ink);
            if(icon!=null)icon.color=Color.Lerp(SceneSettingsSidebarBand.Hex(reset?"A66F68":"8796B5"),Color.white,ink);
            if(marker!=null){marker.enabled=selected;marker.color=open?Color.white:SceneSettingsSidebarBand.Hex("586BCC");}
        }
        public void OnPointerEnter(PointerEventData e){hover=true;}
        public void OnPointerExit(PointerEventData e){hover=false;down=false;}
        public void OnPointerDown(PointerEventData e){down=e.button==PointerEventData.InputButton.Left;}
        public void OnPointerUp(PointerEventData e){down=false;}
        public void OnSelect(BaseEventData e){focused=true;}
        public void OnDeselect(BaseEventData e){focused=false;}
        void OnDisable(){hover=down=focused=false;}
    }
}
