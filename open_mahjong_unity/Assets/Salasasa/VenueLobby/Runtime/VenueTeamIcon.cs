using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
namespace Salasasa.Venues {
    public sealed class VenueTeamIcon : MonoBehaviour {
        public Image image;
        public GameObject frame;
        public bool keepFrameWhenEmpty;
        string source;
        Sprite owned;
        UnityWebRequest request;
        public Action<bool> VisibilityChanged;
        public void Show(string value) {
            value = value ?? "";
            if (source == value && (owned || request != null || string.IsNullOrEmpty(value))) return;
            Cancel(); source = value; SetVisible(false);
            if (value.StartsWith("data:image/", StringComparison.Ordinal)) {
                try { Apply(Convert.FromBase64String(value.Substring(value.IndexOf(',') + 1))); } catch (Exception) { SetVisible(false); }
            } else if (value.StartsWith("/api/venue-icons/", StringComparison.Ordinal) && isActiveAndEnabled) StartCoroutine(Load(value));
        }
        IEnumerator Load(string value) {
            using (var r = UnityWebRequest.Get(ConfigManager.webApiUrl.TrimEnd('/') + value)) {
                request = r; r.timeout = 15; r.redirectLimit = 0;
                yield return r.SendWebRequest();
                if (source == value && r.result == UnityWebRequest.Result.Success && r.downloadHandler.data.Length <= 65536) Apply(r.downloadHandler.data);
                request = null;
            }
        }
        void Apply(byte[] bytes) {
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
            if (!texture.LoadImage(bytes) || texture.width > 512 || texture.height > 512) { Destroy(texture); return; }
            owned = Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
            image.sprite = owned; image.preserveAspect = true; SetVisible(true);
        }
        void SetVisible(bool visible) { if(image)image.enabled=visible; if(frame)frame.SetActive(visible || keepFrameWhenEmpty); VisibilityChanged?.Invoke(visible); }
        void Cancel() { request?.Abort(); StopAllCoroutines(); request=null; if(image)image.sprite=null; if(owned){Destroy(owned.texture);Destroy(owned);owned=null;} }
        void OnDisable(){Cancel(); source=null; SetVisible(false);}
        void OnDestroy(){Cancel();}
    }
}
