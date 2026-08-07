#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器拖拽接收器：挂在常驻对象（场景根节点）上，保证把 Project 里的图片资源
/// 拖进 Game 视图时始终能响应，即使牌背设置面板处于隐藏状态。
/// </summary>
public class CardBackEditorDragReceiver : MonoBehaviour
{
    public static void EnsureOnRoot(GameObject anyObject)
    {
        if (anyObject == null) return;
        GameObject root = anyObject.transform.root.gameObject;
        if (root.GetComponent<CardBackEditorDragReceiver>() == null)
        {
            root.AddComponent<CardBackEditorDragReceiver>();
        }
    }

    private void OnGUI()
    {
        Event e = Event.current;
        if (e.type == EventType.DragUpdated)
        {
            bool hasImage = false;
            foreach (Object obj in DragAndDrop.objectReferences)
            {
                if (obj is Texture2D || obj is Sprite)
                {
                    hasImage = true;
                    break;
                }
            }
            if (hasImage)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
        }
        else if (e.type == EventType.DragPerform)
        {
            bool applied = false;
            foreach (Object obj in DragAndDrop.objectReferences)
            {
                Texture2D tex = obj as Texture2D;
                if (tex == null && obj is Sprite sprite)
                {
                    tex = sprite.texture;
                }
                if (tex == null) continue;

                // 牌边面板激活时拖入的图片应用到侧面贴图，否则作为牌背图片。
                if (CardEdgePanel.Instance != null && CardEdgePanel.Instance.gameObject.activeInHierarchy)
                {
                    CardEdgePanel.Instance.ApplyEditorDroppedTexture(tex);
                }
                else if (CardBackConfigPanel.Instance != null)
                {
                    CardBackConfigPanel.Instance.ApplyEditorDroppedTexture(tex);
                }
                applied = true;
            }
            if (applied)
            {
                DragAndDrop.AcceptDrag();
                e.Use();
            }
        }
    }
}
#endif
