using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Salasasa.Venues {
    public sealed class VenueTeamIconPicker : MonoBehaviour {
        public Button choose, remove;
        public TMP_Text status;
        public bool showOptionalHint = true;
        public VenueTeamIcon preview;
        public string Value { get; private set; } = "";
        public Action<string> Changed;
        int generation;
        bool canEdit=true;
        void Awake(){choose.onClick.AddListener(Pick);remove.onClick.AddListener(Clear);}
        void OnDisable(){generation++;}
        public void Write(string value){Value=value??"";if(preview)preview.Show(Value);SetStatus(showOptionalHint ? (string.IsNullOrEmpty(Value) ? "队徽可选，不上传则不显示" : "已选择队徽") : "");RefreshButtons();}
        public void SetEditable(bool value){canEdit=value;RefreshButtons();}
        void RefreshButtons(){if(choose)choose.interactable=canEdit;if(remove)remove.interactable=canEdit&&!string.IsNullOrEmpty(Value);}
        void Clear(){if(!canEdit)return;generation++;Write("");Changed?.Invoke(Value);}
        void Error(string message){SetStatus(message);}
        void SetStatus(string message){
            bool visible=!string.IsNullOrEmpty(message);
            if(status){status.text=message;status.gameObject.SetActive(visible);}
            if(!showOptionalHint && TryGetComponent<LayoutElement>(out var layout)){
                float previewHeight=preview && preview.frame ? ((RectTransform)preview.frame.transform).rect.height : 40;
                layout.minHeight=layout.preferredHeight=previewHeight+(visible ? 30 : 0);
            }
        }
        public void Pick(){
            if(!canEdit)return;
            int ticket=++generation;
            Action<byte[]> done=bytes=>{if(!this||!isActiveAndEnabled||!canEdit||ticket!=generation)return;try{Write(Encode(bytes));Changed?.Invoke(Value);}catch(Exception e){Error(e.Message);}};
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            LocalAssetPick.ReadFile(LocalAssetPick.ImageFileTypes,(bytes,name)=>done(bytes),Error);
#elif UNITY_WEBGL && !UNITY_EDITOR
            UnityAssetIdb.PickBytes("image/png,image/jpeg",(name,bytes)=>done(bytes),Error);
#else
            var files=SFB.StandaloneFileBrowser.OpenFilePanel("选择队徽", "", new[]{new SFB.ExtensionFilter("PNG / JPEG", "png","jpg","jpeg")},false);
            if(files.Length>0&&!string.IsNullOrEmpty(files[0])){try{if(new FileInfo(files[0]).Length>8*1024*1024)throw new Exception("请选择 8MB 以内的图片");done(File.ReadAllBytes(files[0]));}catch(Exception e){Error(e.Message);}}
#endif
        }
        public static string Encode(byte[] bytes){
            if(bytes==null||bytes.Length>8*1024*1024)throw new Exception("请选择 8MB 以内的 PNG/JPEG 图片");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{
                if(!texture.LoadImage(bytes))throw new Exception("图片无法读取");
                for(int size=256;size>=128;size/=2){
                    float scale=Mathf.Min(1,(float)size/Mathf.Max(texture.width,texture.height));
                    int w=Mathf.Max(1,Mathf.RoundToInt(texture.width*scale)),h=Mathf.Max(1,Mathf.RoundToInt(texture.height*scale));
                    var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;Texture2D small=null;
                    try{Graphics.Blit(texture,rt);RenderTexture.active=rt;small=new Texture2D(w,h,TextureFormat.RGBA32,false);small.ReadPixels(new Rect(0,0,w,h),0,0);small.Apply();var png=small.EncodeToPNG();if(png.Length<=65536)return "data:image/png;base64,"+Convert.ToBase64String(png);}
                    finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(small)Destroy(small);}
                }
                throw new Exception("图片过于复杂，请选择较简单的队徽");
            }finally{Destroy(texture);}
        }
    }
}
