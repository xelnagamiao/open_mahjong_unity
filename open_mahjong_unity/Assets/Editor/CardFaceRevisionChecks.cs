using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只在隔离预览中运行。临时牌组、配置键和静态引用均在 finally 中恢复。</summary>
public static class CardFaceRevisionChecks {
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
    public static string Run(CardFaceConfigPanel face, Action<string,int,int> capture) {
        var report = new StringBuilder();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
        string[] strings = { "CustomTilePackLibraryV1", "StandardTilePackId", "CustomTilePackFileName" };
        string[] ints = { "CustomStandardTilePack", "UseHandFaceBackground", "UseTableFaceBackground", "TableFaceUseSolidColor" };
        var present = strings.Concat(ints).ToDictionary(k => k, PlayerPrefs.HasKey);
        var texts = strings.ToDictionary(k => k, k => PlayerPrefs.GetString(k));
        var numbers = ints.ToDictionary(k => k, k => PlayerPrefs.GetInt(k));
        var created = new List<string>();
        var oldConfig = ConfigManager.Instance;
        var testObject = new GameObject("CardFaceRevisionCheckConfig"); testObject.SetActive(false);
        var config = testObject.AddComponent<ConfigManager>();
        typeof(ConfigManager).GetProperty("Instance", Flags).SetValue(null, config);
        bool oldTableLoaded = (bool)typeof(CardBackManager).GetField("_tableBgLoaded", Flags).GetValue(null);
        var oldTable = CardBackManager.CurrentTableBackground;
        var tableTexture = new Texture2D(220,366);
        tableTexture.name = "VerificationTableBackground";
        var pixels = Enumerable.Repeat(new Color32(160,190,215,255),220*366).ToArray();
        tableTexture.SetPixels32(pixels); tableTexture.Apply();
        try {
            PlayerPrefs.SetString("CustomTilePackLibraryV1", "{\"entries\":[]}");
            Set(config,"StandardTilePackId", TilePackIds.PackOfficial);
            Set(config,"CustomTilePackFileName", "");
            face.ShowPanel();
            string first = Import("自定义示例 A.zip", "fluffy");
            string firstPixels=Convert.ToBase64String(TileFaceResolver.PreviewHand(11).texture.EncodeToPNG());
            string second = Import("自定义示例 B.zip", "hkmahjong");
            string secondPixels=Convert.ToBase64String(TileFaceResolver.PreviewHand(11).texture.EncodeToPNG());
            Check(firstPixels!=secondPixels,"test packs contain distinct face artwork");
            Check(first != second && File.Exists(TilePackLibrary.FilePath(first)) && File.Exists(TilePackLibrary.FilePath(second)), "two imports retain separate saved ZIP files");
            Check(TilePackLibrary.GetEntries().Any(e => e.id == first) && TilePackLibrary.GetEntries().Any(e => e.id == second), "catalog reload retains both custom packs");
            var dropdown = face.GetComponentsInChildren<TMP_Dropdown>(true).Single(d => d.name == "CustomPackDropdown");
            var entries = TilePackLibrary.GetEntries();
            dropdown.value = entries.FindIndex(e => e.id == first) + 1;
            Check(config.StandardTilePackId == first && TileFaceResolver.CountPackFaces() == 1
                && Convert.ToBase64String(TileFaceResolver.PreviewHand(11).texture.EncodeToPNG())==firstPixels, "native dropdown reloads the first saved pack and matching artwork");
            dropdown.value = entries.FindIndex(e => e.id == second) + 1;
            Check(config.StandardTilePackId == second && TileFaceResolver.CountPackFaces() == 1
                && Convert.ToBase64String(TileFaceResolver.PreviewHand(11).texture.EncodeToPNG())==secondPixels, "native dropdown switches back to the second saved pack and matching artwork");
            int count = TilePackLibrary.GetEntries().Count;
            Call(face,"OnZipPicked", new byte[] { 1,2,3 }, "无效.zip");
            Check(TilePackLibrary.GetEntries().Count == count && config.StandardTilePackId == second, "invalid ZIP leaves saved packs and current selection unchanged");
            // Ensure the legacy fixed directory entry survives registering new IDs.
            Set(config,"CustomTilePackFileName","旧版牌组.zip");
            Check(TilePackLibrary.GetEntries().Any(e => e.id == TilePackIds.PackCustom), "legacy single-pack selection remains available");
            Set(config,"CustomTilePackFileName", "");
            Call(face,"RefreshPreview"); Call(dropdown,"Start"); dropdown.alphaFadeSpeed=0; dropdown.Show(); Canvas.ForceUpdateCanvases();
            capture("06_main_custom_pack_list",1280,720); CloseEditorDropdown(dropdown);
            TileFaceResolver.SelectPack(TilePackIds.PackHkMahjong);
            Set(config,"UseTableFaceBackground",true); Set(config,"TableFaceUseSolidColor",false);
            typeof(CardBackManager).GetField("_tableBgLoaded",Flags).SetValue(null,true);
            typeof(CardBackManager).GetProperty("CurrentTableBackground",Flags).SetValue(null,null);
            Call(face,"OnTogglePreview",true);
            Check(TileFaceResolver.PeekTableBackgroundTexture() == null && TileFaceResolver.LoadTableBackground() == null,
                "missing 3D background never resolves to the hand background");
            var slot = face.GetComponentsInChildren<CardFacePreviewSlot>().First(s => s.tileId == 11);
            Check(slot.image.sprite == TileFaceResolver.FlatTableBackground && slot.image.color == ConfigManager.DefaultTableFaceFallbackColor,
                "3D gallery uses its own flat default face color without a hand rim");
            capture("07_main_correct_3d_default",1280,720);
            var tabs=face.GetComponentInParent<CardDesignTabs>();
            string chosenPack=config.StandardTilePackId;
            CardDesignAggregationVerification.ReleasePreviewSprites();tabs.ShowTableBackground();
            Check(tabs.transform.Find("Card3DDesignPanel/TableFacePage").gameObject.activeInHierarchy
                && !face.gameObject.activeInHierarchy,"independent 3D background settings tab opens");
            tabs.ShowFace(0);
            Check(face.ShowingTablePreview && config.StandardTilePackId==chosenPack && config.UseTableFaceBackground,
                "returning to pack page retains selected pack, 3D view and background flag");
            Check(face.transform.Find("StandardViewActions").GetComponentsInChildren<Button>(true).Length == 2
                && face.transform.Find("StatusText") == null
                && face.GetComponentsInChildren<GridLayoutGroup>(true).Count(g=>g.name.EndsWith("PreviewContent"))==2,
                "only hand/3D switches and both complete previews remain on the pack page");
            typeof(CardBackManager).GetProperty("CurrentTableBackground",Flags).SetValue(null,tableTexture);
            TileFaceResolver.NotifyTableBackgroundChanged(); Call(face,"OnTogglePreview",true);
            var backgroundLayer = slot.transform.Find("TableBackgroundLayer").GetComponent<Image>();
            Check(backgroundLayer.gameObject.activeSelf && backgroundLayer.sprite.texture == tableTexture
                && slot.image.sprite == TileFaceResolver.FlatTableBackground && slot.image.color == TileFaceResolver.TablePreviewBaseColor,
                "uploaded 3D background overlays the correct flat gallery base color");
            Set(config,"TableFaceUseSolidColor",true); Set(config,"TableFaceColor",new Color(.15f,.22f,.35f));
            Call(face,"OnTogglePreview",true);
            Check(slot.image.sprite == TileFaceResolver.FlatTableBackground && slot.image.color == config.TableFaceColor
                && !backgroundLayer.gameObject.activeSelf,
                "solid mode suppresses the stored image and shows the 3D face color");
            Call(face,"OnTogglePreview",false);
            Check(slot.image.sprite.texture == TileFaceResolver.PeekHandBackgroundTexture(), "hand view automatically composes its independent background");
            return report.ToString();

            string Import(string name,string pack) {
                byte[] bytes;
                using (var stream = new MemoryStream()) {
                    using (var zip = new ZipArchive(stream,ZipArchiveMode.Create,true))
                        foreach (string type in new[]{"hand","table"}) {
                            var data = File.ReadAllBytes("Assets/Resources/" + TilePackIds.ResourcesPackRoot + "/" + pack + "/" + type + "/11.png");
                            using (var writer = zip.CreateEntry(type+"/11.png").Open()) writer.Write(data,0,data.Length);
                        }
                    bytes=stream.ToArray();
                }
                Call(face,"OnZipPicked",bytes,name);
                string id=config.StandardTilePackId;
                if (!id.StartsWith("custom-",StringComparison.Ordinal)) throw new Exception("Upload did not apply a new library pack");
                created.Add(id); return id;
            }
        } finally {
            TileFaceResolver.SelectPack(TilePackIds.PackOfficial);
            typeof(CardBackManager).GetProperty("CurrentTableBackground",Flags).SetValue(null,oldTable);
            typeof(CardBackManager).GetField("_tableBgLoaded",Flags).SetValue(null,oldTableLoaded);
            TileFaceResolver.NotifyTableBackgroundChanged();
            foreach (var id in created) File.Delete(TilePackLibrary.FilePath(id));
            foreach (var key in strings) { if (present[key]) PlayerPrefs.SetString(key,texts[key]); else PlayerPrefs.DeleteKey(key); }
            foreach (var key in ints) { if (present[key]) PlayerPrefs.SetInt(key,numbers[key]); else PlayerPrefs.DeleteKey(key); }
            PlayerPrefs.Save();
            typeof(ConfigManager).GetProperty("Instance",Flags).SetValue(null,oldConfig);
            UnityEngine.Object.DestroyImmediate(tableTexture); UnityEngine.Object.DestroyImmediate(testObject);
        }
    }

    internal static void CloseEditorDropdown(TMP_Dropdown dropdown)
    {
        // Native Hide uses delayed Destroy, which requires a running player loop.
        // Dispose only this isolated Editor preview's generated list immediately.
        dropdown.StopAllCoroutines();
        foreach (string name in new[] { "m_Dropdown", "m_Blocker" }) {
            var field = typeof(TMP_Dropdown).GetField(name, Flags);
            var instance = field.GetValue(dropdown) as UnityEngine.Object;
            field.SetValue(dropdown, null);
            if (instance) UnityEngine.Object.DestroyImmediate(instance);
        }
        Call(dropdown, "ImmediateDestroyDropdownList");
    }
}
