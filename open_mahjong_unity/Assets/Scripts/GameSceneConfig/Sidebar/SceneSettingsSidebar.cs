using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    // Presentation adapter only. All native SceneConfigPanel listeners stay intact.
    public sealed partial class SceneSettingsSidebar : MonoBehaviour
    {
        public GameObject[] pages;
        public Button[] navigation;
        public SceneSettingsSidebarRow[] rows;
        public Button visibility;
        public SceneSettingsSidebarIcon visibilityIcon;
        public SceneSettingsSidebarRow visibilityRow;
        public int selectedIndex;
        public bool IsOpen {get;private set;}
        void Start(){foreach(var b in navigation)b.onClick.AddListener(Refresh);visibility.onClick.AddListener(Refresh);Refresh();}
        void OnDestroy()
        {
            foreach(var button in navigation)if(button!=null)button.onClick.RemoveListener(Refresh);
            if(visibility!=null)visibility.onClick.RemoveListener(Refresh);
        }
        void LateUpdate()=>Refresh();
        public void Refresh()
        {
            IsOpen=false;
            for(int i=0;i<pages.Length;i++)if(pages[i].activeInHierarchy){selectedIndex=i;IsOpen=true;break;}
            for(int i=0;i<rows.Length;i++){rows[i].selected=i==selectedIndex;rows[i].open=IsOpen;}
            if(visibilityRow!=null){visibilityRow.selected=!IsOpen;visibilityRow.open=true;}
            if(visibilityIcon.kind!=(IsOpen?7:8)){visibilityIcon.kind=IsOpen?7:8;visibilityIcon.SetVerticesDirty();}
        }
        public void DrawImmediate(){Refresh();foreach(var row in GetComponentsInChildren<SceneSettingsSidebarRow>(true))row.Draw(true);}
    }
}
