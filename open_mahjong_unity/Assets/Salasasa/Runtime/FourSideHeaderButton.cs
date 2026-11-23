using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Salasasa.Home
{
    public sealed class FourSideHeaderButton:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        public FourSideHeader navigation;
        public string route;
        public Button button;
        public RectTransform artwork;
        public HeaderButtonPresentation presentation;
        public Image selected;
        public FourSideHeaderSelection cornerSelection;
        public bool compactIcon;
        public bool IsSelected { get; private set; }
        float scale=1;
        bool hover,pressed;
        private RecordPanel preparingRecords;
        private Coroutine prepareRoutine;
        void Awake(){button.onClick.AddListener(Activate);}
        void OnDestroy(){if(button!=null)button.onClick.RemoveListener(Activate);}
        public void Activate()=>navigation.Execute(route);
        public void SetSelected(bool value)
        {
            IsSelected=value;
            if(selected!=null)selected.enabled=value&&cornerSelection==null;
            if(cornerSelection!=null)cornerSelection.Apply(value,hover,pressed);
            if(presentation)presentation.ApplyInk();
        }
        public void Fit(){var r=(RectTransform)transform;if(presentation)presentation.Apply();scale=presentation?Mathf.Min(1,r.rect.height/(compactIcon?44:64)):Mathf.Min(r.rect.width/(compactIcon?44:72),r.rect.height/(compactIcon?44:64));Refresh();}
        void Refresh()
        {
            artwork.localScale=Vector3.one*scale*(pressed?.96f:hover?1.025f:1);
            if(cornerSelection!=null)cornerSelection.Apply(IsSelected,hover,pressed);
            if(presentation)presentation.ApplyInk();
        }
        public void OnPointerEnter(PointerEventData e){hover=true;Refresh();}
        public void OnPointerExit(PointerEventData e){hover=pressed=false;EndRecordPreparation(true);Refresh();}
        public void OnPointerDown(PointerEventData e){
            pressed=true;Refresh();
            var windows=WindowsManager.Instance;
            if(route!="history" || e.button!=PointerEventData.InputButton.Left || !button.IsInteractable() ||
                !windows || windows.GetCurrentWindow()=="record" || preparingRecords) return;
            preparingRecords=windows.RecordListForNavigation;
            if(preparingRecords) prepareRoutine=StartCoroutine(preparingRecords.PrepareForNavigation());
        }
        public void OnPointerUp(PointerEventData e){
            pressed=false;
            var hit=e.pointerCurrentRaycast.gameObject;
            EndRecordPreparation(!e.eligibleForClick || !hit || !hit.transform.IsChildOf(transform));
            Refresh();
        }
        private void EndRecordPreparation(bool cancel){
            if(prepareRoutine!=null)StopCoroutine(prepareRoutine);
            prepareRoutine=null;
            if(cancel && preparingRecords)preparingRecords.CancelNavigationPreparation();
            preparingRecords=null;
        }
        void OnDisable(){EndRecordPreparation(true);hover=pressed=false;if(cornerSelection!=null)cornerSelection.Apply(IsSelected,false,false);}
    }
}
