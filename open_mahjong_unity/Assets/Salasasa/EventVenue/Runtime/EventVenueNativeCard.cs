using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Events
{
    public sealed class EventVenueNativeCard : MonoBehaviour
    {
        public string kind,sourceGuid;
        public Graphic[] graphics;
        public Color[] originalColors,themedColors;
        Color[] liveOriginal;
        public void CaptureOriginal()
        {
            liveOriginal=new Color[graphics.Length];
            for(int i=0;i<graphics.Length;i++)liveOriginal[i]=graphics[i]?graphics[i].color:Color.clear;
        }
        public void Apply(bool original)
        {
            if(liveOriginal==null)CaptureOriginal();
            var colors=original?liveOriginal:themedColors;
            for(int i=0;i<graphics.Length&&i<colors.Length;i++)
            {
                var g=graphics[i];
                if(!g || g.name=="Star" || g.transform.parent?.name=="FavoriteButton" && g is TMPro.TMP_Text)continue;
                g.color=colors[i];
            }
        }
    }
}
