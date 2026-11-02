#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Salasasa.Home
{
    // Optional artwork is resolved only in the Editor or while constructing a development Player.
    // The saved scene retains its required defaults and never depends on this local image pack.
    public static class DevelopmentArtworkCatalog
    {
        public const string Root = "Assets/Salasasa/PreviewArt/";
        public const string CatalogPath = Root + "development-artwork-catalog.json";
        public const string DefaultCharacter = Root + "TaiseiCharacters/taisei-reimu.png";
        public const string DefaultBackground = Root + "BackgroundLibrary/minchi-cafe-1.jpg";
        const string ExpressionRoot = Root + "TaiseiCharacters/Resources/";

        [Serializable]
        public sealed class Entry
        {
            public string characterPath;
            public string backgroundPath;
            public LobbyArtwork artwork;
        }

        [Serializable]
        public sealed class Catalog
        {
            public int version = 1;
            public string selectedCharacterPath;
            public string selectedBackgroundPath;
            public Entry[] artworkSets;
            public Entry[] characterSets;
            public Entry[] backgroundSets;
        }

        public static Catalog Capture(LobbyShowcase owner)
        {
            return new Catalog {
                selectedCharacterPath = AssetDatabase.GetAssetPath(owner.CurrentArtwork?.character),
                selectedBackgroundPath = AssetDatabase.GetAssetPath(owner.CurrentBackgroundArtwork?.background),
                artworkSets = (owner.artworkSets ?? Array.Empty<LobbyArtwork>()).Select(Capture).ToArray(),
                characterSets = (owner.characterSets ?? Array.Empty<LobbyArtwork>()).Select(Capture).ToArray(),
                backgroundSets = (owner.backgroundSets ?? Array.Empty<LobbyArtwork>()).Select(Capture).ToArray()
            };
        }

        static Entry Capture(LobbyArtwork source)
        {
            if (source == null) return null;
            var entry = new Entry {
                characterPath = AssetDatabase.GetAssetPath(source.character),
                backgroundPath = AssetDatabase.GetAssetPath(source.background),
                artwork = Clone(source)
            };
            entry.artwork.character = entry.artwork.background = null;
            return entry;
        }

        public static LobbyArtwork Clone(LobbyArtwork source)
            => JsonUtility.FromJson<LobbyArtwork>(JsonUtility.ToJson(source));

        public static bool LoadInto(LobbyShowcase owner)
        {
            if (owner == null || !File.Exists(CatalogPath)) return false;
            Catalog catalog;
            try { catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath)); }
            catch (Exception exception) {
                Debug.LogWarning("开发素材清单无法读取，使用默认素材：" + exception.Message);
                return false;
            }
            if (catalog == null || catalog.version != 1) return false;
            Apply(owner, catalog, path => AssetDatabase.LoadAssetAtPath<Texture2D>(path), File.Exists);
            return true;
        }

        // Passing a resolver also permits validating a fresh checkout without removing local files.
        public static void Apply(LobbyShowcase owner, Catalog catalog, Func<string, Texture2D> resolve, Func<string, bool> exists)
        {
            var pairs = Resolve(catalog.artworkSets, resolve, exists, true, true);
            var characters = Resolve(catalog.characterSets, resolve, exists, true, false);
            var backgrounds = Resolve(catalog.backgroundSets, resolve, exists, false, true);
            if (pairs.Length > 0) owner.artworkSets = pairs;
            if (characters.Length > 0) owner.characterSets = characters;
            if (backgrounds.Length > 0) owner.backgroundSets = backgrounds;
            owner.independentArtworkSelection = true;
            owner.selectedArtworkIndex = FindIndex(owner.characterSets, catalog.selectedCharacterPath, DefaultCharacter, true);
            owner.selectedBackgroundIndex = FindIndex(owner.backgroundSets, catalog.selectedBackgroundPath, DefaultBackground, false);
            owner.characterArtwork = owner.backgroundArtwork = null;
        }

        static LobbyArtwork[] Resolve(Entry[] entries, Func<string, Texture2D> resolve, Func<string, bool> exists, bool requireCharacter, bool requireBackground)
        {
            return (entries ?? Array.Empty<Entry>()).Where(entry => entry?.artwork != null).Select(entry => {
                var art = Clone(entry.artwork);
                art.character = string.IsNullOrEmpty(entry.characterPath) ? null : resolve(entry.characterPath);
                art.background = string.IsNullOrEmpty(entry.backgroundPath) ? null : resolve(entry.backgroundPath);
                if ((requireCharacter && !art.character) || (requireBackground && !art.background)) return null;
                art.expressions = (art.expressions ?? Array.Empty<LobbyExpression>()).Where(expression =>
                    expression != null && (string.IsNullOrEmpty(expression.resourcePath) ||
                    exists(ExpressionRoot + expression.resourcePath + ".png"))).ToArray();
                return art;
            }).Where(art => art != null).ToArray();
        }

        static int FindIndex(LobbyArtwork[] entries, string preferred, string fallback, bool character)
        {
            if (entries == null) return 0;
            int Match(string path) => Array.FindIndex(entries, entry => entry != null &&
                AssetDatabase.GetAssetPath(character ? entry.character : entry.background) == path);
            int index = string.IsNullOrEmpty(preferred) ? -1 : Match(preferred);
            return Mathf.Max(0, index >= 0 ? index : Match(fallback));
        }

        public static void BakeDefaults(LobbyShowcase owner)
        {
            var entries = (owner.characterSets ?? Array.Empty<LobbyArtwork>())
                .Concat(owner.backgroundSets ?? Array.Empty<LobbyArtwork>())
                .Concat(owner.artworkSets ?? Array.Empty<LobbyArtwork>()).Where(art => art != null).ToArray();
            var character = entries.FirstOrDefault(art => AssetDatabase.GetAssetPath(art.character) == DefaultCharacter);
            var background = entries.FirstOrDefault(art => AssetDatabase.GetAssetPath(art.background) == DefaultBackground);
            if (character == null || background == null)
                throw new InvalidOperationException("主场景必须包含默认灵梦和咖啡馆背景，不能用空白素材保存。");
            var defaults = Clone(character);
            defaults.character = character.character;
            defaults.background = background.background;
            defaults.sceneName = background.sceneName;
            defaults.collection = background.collection;
            defaults.variant = background.variant;
            defaults.category = background.category;
            defaults.backgroundCredit = background.backgroundCredit;
            defaults.backgroundSourceUrl = background.backgroundSourceUrl;
            defaults.backgroundTint = background.backgroundTint;
            defaults.shadeLeft = background.shadeLeft;
            defaults.shadeRight = background.shadeRight;
            defaults.expressions = Array.Empty<LobbyExpression>();
            owner.artworkSets = new[] { defaults };
            owner.characterSets = new[] { defaults };
            owner.backgroundSets = new[] { defaults };
            owner.independentArtworkSelection = true;
            owner.selectedArtworkIndex = owner.selectedBackgroundIndex = 0;
            owner.characterArtwork = owner.backgroundArtwork = null;
            owner.RefreshArtwork();
        }
    }
}
#endif
