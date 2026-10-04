using TMPro;
using UnityEngine;

/// <summary>Retained cloth header adapter; cloth and frame share the same create-color workflow.</summary>
public sealed class TableClothColorControl : MonoBehaviour
{
    [SerializeField] TableSurfaceColorEditor editor;
#if UNITY_EDITOR
    public void Initialize(RectTransform host, RectTransform headerRect, TableClothPanel owner, TMP_FontAsset font)
    {
        editor=TableSurfaceColorEditor.Ensure(owner);
        editor.InitializeControl(host,font);
    }
#endif
    public void RefreshSelection()=>editor?.RefreshSelection();
}
