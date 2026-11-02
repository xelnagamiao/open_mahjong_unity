using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Salasasa.Matchmaking
{
    public sealed class MatchPageLayouts : MonoBehaviour
    {
        public MatchLobbyView[] views;
        public GameObject[] roots;
        public RectTransform sidePanel;
        public CanvasGroup sideOpacity;
        public Button[] layoutButtons;
        public Graphic[] selectedLayouts;
        public int CurrentLayout { get; private set; }
        float slide=1;
        bool initialized;

        void Awake()
        {
            for(int i=0;i<layoutButtons.Length;i++) { int index=i%2; layoutButtons[i].gameObject.SetActive(index==0); if(index==0)layoutButtons[i].onClick.AddListener(()=>SelectLayout(0)); }
            CurrentLayout=0;
            PlayerPrefs.SetInt("SalasasaMatchLayout",0);
            initialized=true;
        }
        void OnEnable() { if(initialized)Apply(true); }
        public void SelectLayout(int index)
        {
            index=0;
            if(index==CurrentLayout)return;
            views[index].CopyNavigationFrom(views[CurrentLayout]);
            CurrentLayout=index;
            PlayerPrefs.SetInt("SalasasaMatchLayout",index);
            Apply(true);
        }
        void Apply(bool animate)
        {
            for(int i=0;i<roots.Length;i++)roots[i].SetActive(i==CurrentLayout);
            for(int i=0;i<selectedLayouts.Length;i++)selectedLayouts[i].gameObject.SetActive(i%2==CurrentLayout);
            slide=CurrentLayout==1&&animate?0:1;
            views[CurrentLayout].RefreshState();
            ApplySlide();
        }
        void Update()
        {
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Close();
            if(CurrentLayout!=1||slide>=1)return;
            slide=Mathf.MoveTowards(slide,1,Time.unscaledDeltaTime/.34f);
            ApplySlide();
        }
        void ApplySlide()
        {
            float eased=1-Mathf.Pow(1-slide,3);
            sidePanel.anchoredPosition=new Vector2((1-eased)*(sidePanel.rect.width+24),0);
            sideOpacity.interactable=slide>.75f;
            sideOpacity.blocksRaycasts=true;
            sideOpacity.alpha=1;
        }
        public void Close() { Salasasa.Home.CompleteLobbyController owner=FindAnyObjectByType<Salasasa.Home.CompleteLobbyController>();if(owner!=null)owner.OpenHome(); }
        void OnDisable() { slide=1;if(sidePanel)ApplySlide(); }
    }
}
