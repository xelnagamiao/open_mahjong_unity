using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CardDesignImageOption : MonoBehaviour
{
    public Image surface, picture;
    public TMP_Text caption;
    public CardSurfaceOptionView selection;
    Texture2D texture;
    Sprite sprite;
    string path;

    public void SetEntry(CardDesignImageLibrary.Entry entry)
    {
        caption.text = entry.name;
        if (path == entry.path) return;
        Release(); path = entry.path;
        texture = CardDesignImageLibrary.LoadTexture(entry.path);
        if (texture != null) sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect);
        surface.color = Color.white; surface.sprite = null;
        picture.sprite = sprite; picture.preserveAspect = true; picture.enabled = sprite != null;
    }
    public void SetSolid(Color color) { caption.text = "纯色"; surface.sprite = null; surface.color = color; picture.enabled = false; }
    public void SetSelected(bool selected) => selection.SetSelected(selected);
    void OnDestroy() => Release();
    void Release()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
        sprite = null; texture = null;
    }
}
