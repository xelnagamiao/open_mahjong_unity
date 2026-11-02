using System;
using System.Linq;
using Salasasa.DataLab;
using Salasasa.Home;
using UnityEngine;

namespace Salasasa.Statistics
{
    [DefaultExecutionOrder(250)]
    public sealed class StatisticsLobbyBridge : MonoBehaviour
    {
        public CompleteLobbyController owner;
        public HomeDataPanel panel;
        public Canvas canvas;
        public RectTransform viewport;
        public BakedModalFade entryFade;
        public bool IsAnimating=>entryFade&&entryFade.IsAnimating;
        bool closing;
        float enteringUntil;
        public bool IsOpen => panel && panel.pageRoot.activeSelf;

        void Awake() { panel.Closed += OnClosed; }
        void OnDestroy() { if(panel)panel.Closed -= OnClosed; }
        void OnClosed() { if(!closing && owner)owner.OpenHome(); }

        public void Open()
        {
            if(transform.IsChildOf(owner.pageRoot))transform.SetAsLastSibling();
            enteringUntil=Time.unscaledTime+1;
            panel.Open();
            ApplyInsets();
        }
        public void Close()
        {
            if(!IsOpen)return;
            closing=true;panel.Close();closing=false;
        }
        public bool HandleEscape() => IsOpen && panel.HandleEscape();
        void LateUpdate()
        {
            if(!IsOpen)return;
            var window=WindowsManager.Instance?.GetCurrentWindow();
            if(window=="login" || Time.unscaledTime>enteringUntil && window!="menu") {Close();return;}
            ApplyInsets();
        }
        public void ApplyInsets()
        {
            var inset=owner.headerNavigation?owner.headerNavigation.Insets:new Vector4(0,68,0,0);
            if(canvas.transform.IsChildOf(owner.pageRoot))inset=Vector4.zero;
            float scale=CanvasReferenceLayout.EffectiveScale(owner.artwork.canvas)/CanvasReferenceLayout.EffectiveScale(canvas);
            viewport.offsetMin=new Vector2(inset.x,inset.w)*scale;
            viewport.offsetMax=new Vector2(-inset.z,-inset.y)*scale;
        }
    }
}
