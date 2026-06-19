using UnityEngine;

using UnityEngine.UI;

using UnityEngine.EventSystems;



public class StaticCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {

    [SerializeField] private Image tileImage;

    

    private int tileId = -1;

    private bool hasDangerTint;

    private bool hasZimoTint;

    private static readonly Color DangerTintColor = new Color(1f, 0.65f, 0.65f, 1f);

    private static readonly Color ZimoTintColor = new Color(0.65f, 0.8f, 1f, 1f);



    public int TileId => tileId;



    public void SetTileOnlyImage(int tile) {

        tileId = tile;

        hasDangerTint = false;

        hasZimoTint = false;

        int faceResourceId = tile;

        if (ConfigManager.Instance.UseBlankWhiteDragonFace(tile)) {

            faceResourceId = ConfigManager.BlankFaceImageId;

        }

        string path = $"image/CardFaceImage_xuefun/{faceResourceId}";

        Sprite sprite = Resources.Load<Sprite>(path);

        if (sprite != null) {

            tileImage.sprite = sprite;

            ApplyWallVisual(1f, false, false);

        } else {

            Debug.LogError($"找不到牌面图片: {path}");

        }

    }



    public void SetTileImageColor(Color color) {

        if (tileImage == null) return;

        tileImage.color = color;

    }



    public void SetOpacity(float alpha) {

        ApplyWallVisual(alpha, hasDangerTint, hasZimoTint);

    }



    public void SetDangerTint(bool on) {

        hasDangerTint = on;

        float alpha = tileImage != null ? tileImage.color.a : 1f;

        ApplyWallVisual(alpha, on, hasZimoTint);

    }



    public void ClearDangerTint() {

        hasDangerTint = false;

        float alpha = tileImage != null ? tileImage.color.a : 1f;

        ApplyWallVisual(alpha, false, hasZimoTint);

    }



    public void ClearWallTints() {

        hasDangerTint = false;

        hasZimoTint = false;

        float alpha = tileImage != null ? tileImage.color.a : 1f;

        ApplyWallVisual(alpha, false, false);

    }



    public void ApplyWallVisual(float alpha, bool dangerTint, bool zimoTint) {

        if (tileImage == null) return;

        hasDangerTint = dangerTint;

        hasZimoTint = zimoTint;



        Color c = Color.white;

        if (dangerTint) {

            c = DangerTintColor;

        }

        else if (zimoTint) {

            c = ZimoTintColor;

        }

        c.a = Mathf.Clamp01(alpha);

        tileImage.color = c;

    }



    public void OnPointerEnter(PointerEventData eventData) {

        if (tileId != -1 && Card3DHoverManager.Instance != null) {

            Card3DHoverManager.Instance.OnCardHover(tileId);

        }

    }



    public void OnPointerExit(PointerEventData eventData) {

        if (Card3DHoverManager.Instance != null) {

            Card3DHoverManager.Instance.OnCardExit();

        }

    }

}


