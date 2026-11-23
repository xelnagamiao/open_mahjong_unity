using System.Collections.Generic;
using UnityEngine;

/// <summary>虹雀与拓展官方包的稳定身份；拓展的来源编码避免不同规则复用牌号。</summary>
public static class FixedFacePack
{
    public const string Hongque = "hongque-standard", Expanded = "expanded-standard";
    static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
    public static bool IsFixed(string pack) => pack == Hongque || pack == Expanded;
    public static string Pack(CardFaceFamilyPreview.Source source) => source == CardFaceFamilyPreview.Source.Hongque ? Hongque : Expanded;
    public static int Key(CardFaceFamilyPreview.Source source, int id) => source == CardFaceFamilyPreview.Source.Hongque
        ? id : 100000 + (int)source * 10000 + id;
    public static void Decode(string pack, int key, out CardFaceFamilyPreview.Source source, out int id) {
        source = pack == Hongque ? CardFaceFamilyPreview.Source.Hongque : (CardFaceFamilyPreview.Source)((key - 100000) / 10000);
        id = pack == Hongque ? key : (key - 100000) % 10000;
    }
    public static bool IsEditable(string pack, int key) {
        if (pack == Hongque) return HongqueTileVisual.IsHongqueId(key);
        if (pack != Expanded || key < 110000 || key >= 150000) return false;
        Decode(pack, key, out var source, out int id);
        return source == CardFaceFamilyPreview.Source.Singapore ? SingaporeTileVisual.IsAnimal(id)
            : source == CardFaceFamilyPreview.Source.Malaysia ? MalaysiaTileVisual.Contains(id)
            : source == CardFaceFamilyPreview.Source.Vietnamese ? VietnameseTileVisual.IsExtra(id)
            : source == CardFaceFamilyPreview.Source.Duocai && id >= 1000 && id < 5040;
    }
    public static bool TryResolve(int tileId, out string pack, out int key) {
        pack = null; key = tileId;
        CardFaceFamilyPreview.Source source;
        if (DuocaiTileVisual.IsCurrent(tileId)) source = CardFaceFamilyPreview.Source.Duocai;
        else if (HongqueTileVisual.IsHongqueId(tileId)) source = CardFaceFamilyPreview.Source.Hongque;
        else if (VietnameseTileVisual.IsCurrentRuleTile(tileId)) source = CardFaceFamilyPreview.Source.Vietnamese;
        else if (MalaysiaTileVisual.Contains(tileId) && RuleRegistry.Current?.RuleId == "malaysia") source = CardFaceFamilyPreview.Source.Malaysia;
        else if (SingaporeTileVisual.IsAnimal(tileId)) source = CardFaceFamilyPreview.Source.Singapore;
        else if (MalaysiaTileVisual.Contains(tileId)) source = CardFaceFamilyPreview.Source.Malaysia;
        else return false;
        pack = Pack(source); key = Key(source, tileId); return true;
    }
    public static Sprite Load(string pack, int key, bool table) {
        Decode(pack, key, out var source, out int id);
        if (source == CardFaceFamilyPreview.Source.Duocai && !DuocaiTiles.Joker(id)) return DuocaiTileVisual.Sprite(id, table);
        string path = source == CardFaceFamilyPreview.Source.Hongque ? (table ? HongqueTileVisual.TableResourcePath(id) : "hongque/hand/" + id)
            : source == CardFaceFamilyPreview.Source.Singapore ? "image/Cards/Faces/singapore/" + id
            : source == CardFaceFamilyPreview.Source.Malaysia ? "Malaysia/" + id
            : source == CardFaceFamilyPreview.Source.Vietnamese ? "Vietnamese/Tiles/" + id : "Duocai/Tiles/" + id;
        if (Sprites.TryGetValue(path, out var sprite)) return sprite;
        if (source == CardFaceFamilyPreview.Source.Hongque && !table) {
            sprite = HongqueTileVisual.LoadSprite(id, cacheForGame: false);
            if (sprite != null) Sprites[path] = sprite;
            return sprite;
        }
        if (source == CardFaceFamilyPreview.Source.Hongque && table) {
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null) return null;
            // Match the real 3D card's UV window, independent of its compressed texture dimensions.
            float height = texture.height / HongqueTileVisual.TableVerticalScale;
            sprite = Sprite.Create(texture, new Rect(0, (texture.height - height) * .5f, texture.width, height),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return Sprites[path] = sprite;
        }
        sprite = Resources.Load<Sprite>(path);
        if (sprite == null) {
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null) return null;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }
        return Sprites[path] = sprite;
    }

    internal static void ReleasePreview(bool hongque) {
        if (Sprites.Count == 0) return;
        Sprites.Clear();
        // A gallery can be closed while a replay remains suspended. Keep the shared
        // hand atlas in that case. A real teardown releases it after detaching tiles.
        if (hongque && !HongqueGameState.IsTableActive) BuiltinTileSprites.ReleaseHongqueHandCache();
        // Gameplay objects can still hold the same texture. Let Unity unload only unreferenced assets.
        SceneAppearanceMemory.RequestCleanup();
    }
    public static bool TryGetArtworkAspect(int tileId, string pack, bool table, out float aspect) {
        aspect = 0f;
        if (!IsFixed(pack) && !TryResolve(tileId, out pack, out tileId)) return false;
        Decode(pack, tileId, out var source, out int id);
        if (source == CardFaceFamilyPreview.Source.Vietnamese) aspect = VietnameseTileVisual.ArtworkAspect;
        else if (source == CardFaceFamilyPreview.Source.Duocai && DuocaiTiles.Joker(id)) aspect = DuocaiTileVisual.JokerArtworkAspect;
        // Hongque's preview sprite already contains the same UV crop as its 3D model.
        else if (table && source == CardFaceFamilyPreview.Source.Hongque) aspect = TileTextureLayout.TableAspect;
        return aspect > 0f;
    }
    public static void ApplyPreview(CardFacePreviewSlot slot, string pack, int key, bool table) {
        slot.packId = pack; slot.tileId = key;
        var sprite = Load(pack, key, table);
        if (table) {
            var config = ConfigManager.Instance;
            var background = config != null && config.UseTableFaceBackground && !config.TableFaceUseSolidColor
                ? TileFaceResolver.LoadTableBackground() : null;
            slot.ApplyTable(sprite, background, TileFaceResolver.TablePreviewBaseColor, false, 1f);
        } else if (pack == Hongque) slot.ApplyStandaloneHand(sprite);
        else slot.Apply(sprite, TileFaceResolver.LoadHandBackground(), false);
    }
    public static string DisplayName(string pack, int key) {
        Decode(pack, key, out var source, out int id);
        if (source == CardFaceFamilyPreview.Source.Hongque) {
            string[] colors = { "红", "红橙", "橙", "橙黄", "黄", "黄绿", "绿", "绿青", "青", "青蓝", "蓝", "蓝紫", "紫", "紫红", "无色" };
            return colors[(id - 1000) / 10] + " · " + id % 10;
        }
        if (source == CardFaceFamilyPreview.Source.Vietnamese) return "越南 · " + VietnameseTileVisual.Name(id);
        if (source == CardFaceFamilyPreview.Source.Duocai) return "夺彩百搭 · " + (id - 4999);
        string region = source == CardFaceFamilyPreview.Source.Singapore ? "新加坡" : "马来西亚";
        return region + " · " + (id >= 61 && id <= 64 ? new[] { "猫", "鼠", "鸡", "蜈蚣" }[id - 61]
            : id == 81 ? "飞" : "人头" + (id - 70));
    }
}
