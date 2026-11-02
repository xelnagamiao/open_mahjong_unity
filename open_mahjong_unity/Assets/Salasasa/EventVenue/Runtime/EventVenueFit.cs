using UnityEngine;

namespace Salasasa.Events
{
    [ExecuteAlways]
    public sealed class EventVenueFit : MonoBehaviour
    {
        public Vector2 referenceSize = new Vector2(1600,900);
        Vector2 previous;
        void OnEnable() { Fit(); }
        void LateUpdate() { Fit(); }
        void Fit()
        {
            var parent=transform.parent as RectTransform;
            if(!parent)return;
            var size=parent.rect.size;
            if(size==previous && transform.localScale.x>0)return;
            previous=size;
            float scale=Mathf.Min(size.x/referenceSize.x,size.y/referenceSize.y);
            if(scale>0)transform.localScale=new Vector3(scale,scale,1);
        }
    }
}
