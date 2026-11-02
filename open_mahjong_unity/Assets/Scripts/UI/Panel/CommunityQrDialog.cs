using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Salasasa.Community {
    public sealed class CommunityQrDialog : MonoBehaviour {
        public TMP_Text title,caption,status,example;
        public UnityEngine.UI.RawImage image;
        public UnityEngine.UI.Button close,dismiss;
        public CanvasGroup group;
        PlatformContentClient client;
        CommunityEntry entry;
        Texture2D texture;
        UnityWebRequest request;
        Coroutine loader;
        Vector2 layoutSize;
        void Awake(){close.onClick.AddListener(Close);dismiss.onClick.AddListener(Close);}
        void OnEnable(){ConfigManager.OnLanguageChanged+=RefreshLanguage;RefreshLanguage();}
        void OnDisable(){ConfigManager.OnLanguageChanged-=RefreshLanguage;}
        void RefreshLanguage(){if(entry==null)return;title.text=entry.DisplayTitle;LayoutContent();}
        public void Open(CommunityEntry value,PlatformContentClient source){
            Close();entry=value;client=source;client.Changed+=ContentChanged;
            gameObject.SetActive(true);transform.SetAsLastSibling();group.alpha=0;
            title.text=value.DisplayTitle;caption.text="";caption.gameObject.SetActive(false);example.text=value.qrIsExample?"示例二维码":"";example.gameObject.SetActive(value.qrIsExample);image.gameObject.SetActive(false);status.text="正在加载二维码…";
            LayoutContent();
            loader=StartCoroutine(Load());
        }
        public void RefreshLayout()=>LayoutContent();
        void LayoutContent(){
            const float width=720, imageWidth=656, imageHeight=824, imageTop=92;
            float titleHeight=Mathf.Max(58,title.GetPreferredValues(title.text,602,0).y+4);
            float extra=titleHeight-58;
            var frame=(RectTransform)image.transform.parent.parent;
            layoutSize=((RectTransform)frame.parent).rect.size;
            frame.sizeDelta=new Vector2(width,944+extra+(example.gameObject.activeSelf?48:0));
            var header=FindAnyObjectByType<Salasasa.Home.FourSideHeader>(FindObjectsInactive.Include);
            var insets=header?header.Insets*CanvasReferenceLayout.EffectiveScale(header.canvas)/CanvasReferenceLayout.EffectiveScale(GetComponentInParent<Canvas>()):Vector4.zero;
            frame.anchoredPosition=new Vector2((insets.x-insets.z)/2,(insets.w-insets.y)/2);
            frame.localScale=Vector3.one*Mathf.Min(1,(layoutSize.x-insets.x-insets.z-48)/width,(layoutSize.y-insets.y-insets.w-48)/frame.sizeDelta.y);
            title.rectTransform.sizeDelta=new Vector2(602,titleHeight);
            ((RectTransform)close.transform).anchoredPosition=new Vector2(width-74,-24);
            var imageFrame=(RectTransform)image.transform.parent;
            imageFrame.anchoredPosition=new Vector2(32,-imageTop-extra);
            imageFrame.sizeDelta=new Vector2(imageWidth,imageHeight);
            example.rectTransform.anchoredPosition=new Vector2(28,-928-extra);
            example.rectTransform.sizeDelta=new Vector2(width-56,36);
            caption.text="";caption.gameObject.SetActive(false);
        }
        void LateUpdate(){if(layoutSize!=((RectTransform)transform).rect.size)LayoutContent();}
        IEnumerator Load(){
            string url=client.ResolveQr(entry.qrUrl);
            if(url==null){status.text="二维码暂时无法加载";yield break;}
            using(var current=UnityWebRequestTexture.GetTexture(url,true)){
                request=current;current.timeout=8;current.redirectLimit=0;yield return current.SendWebRequest();
                if(current.result==UnityWebRequest.Result.Success){texture=DownloadHandlerTexture.GetContent(current);if(texture.width<=2048&&texture.height<=2048){texture.filterMode=FilterMode.Point;image.texture=texture;image.GetComponent<UnityEngine.UI.AspectRatioFitter>().aspectRatio=texture.width/(float)texture.height;image.gameObject.SetActive(true);status.text="";}else status.text="二维码尺寸不受支持";}
                else status.text="二维码暂时无法加载";
            }
            request=null;loader=null;
        }
        void Update(){group.alpha=Mathf.MoveTowards(group.alpha,1,Time.unscaledDeltaTime/.16f);if(entry!=null&&!entry.Available)Close();if(Input.GetKeyDown(KeyCode.Escape))Close();}
        void ContentChanged(){var current=client.Document.communities.FirstOrDefault(x=>x.id==entry.id);if(current==null||!current.Available||current.qrUrl!=entry.qrUrl)Close();else{entry=current;RefreshLanguage();}}
        public void Close(){if(client!=null)client.Changed-=ContentChanged;if(request!=null)request.Abort();if(loader!=null)StopCoroutine(loader);request=null;loader=null;if(texture)Destroy(texture);texture=null;image.texture=null;entry=null;client=null;gameObject.SetActive(false);}
        void OnDestroy(){if(client!=null)client.Changed-=ContentChanged;if(texture)Destroy(texture);}
    }
}
