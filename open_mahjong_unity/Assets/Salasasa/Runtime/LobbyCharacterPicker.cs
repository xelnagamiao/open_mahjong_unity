using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    public sealed partial class LobbyOverlay
    {
        const int CharactersPerPage = 6;
        static readonly string[] CharacterCollections = { "全部", "Taisei", "原有" };
        int characterPage;
        string characterCollection = "Taisei";

        void CharacterGallery()
        {
            var filtered = new List<int>();
            for (int i = 0; i < owner.CharacterCount; i++)
            {
                string collection = string.IsNullOrEmpty(owner.CharacterAt(i).collection) ? "原有" : owner.CharacterAt(i).collection;
                if (characterCollection == "全部" || characterCollection == collection) filtered.Add(i);
            }
            int pages = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)CharactersPerPage));
            characterPage = Mathf.Clamp(characterPage, 0, pages - 1);
            for (int i = 0; i < CharacterCollections.Length; i++)
            {
                string collection = CharacterCollections[i];
                Button("CharacterCollection" + i, collection, 30 + i * 132, 218, 120, 34,
                    () => { characterCollection = collection; characterPage = 0; RefreshBackgroundPicker(); },
                    characterCollection == collection);
            }
            int start = characterPage * CharactersPerPage;
            for (int slot = 0; slot < CharactersPerPage && start + slot < filtered.Count; slot++)
            {
                int index = filtered[start + slot];
                var art = owner.CharacterAt(index);
                bool selected = owner.selectedArtworkIndex == index;
                var card = Button("CharacterChoice" + index, "", 30 + slot % 3 * 198,
                    266 + slot / 3 * 146, 184, 136, () => owner.SelectCharacter(index), selected);
                TrackArtworkChoice(card, () => owner.selectedArtworkIndex == index);
                CharacterThumbnail(card.transform, art);
                var name = LobbyUI.Text(card.transform, "Name", art.characterName,
                    6, 111, 172, 22, 16, selected ? T.accentInk : T.ink, T.font);
                name.alignment = TextAlignmentOptions.Center;
                name.enableAutoSizing = true; name.fontSizeMin = 12; name.fontSizeMax = 16;
            }
            var previous = Button("CharacterPreviousPage", "上一页", 30, 568, 150, 44,
                () => { characterPage--; RefreshBackgroundPicker(); });
            previous.interactable = characterPage > 0;
            Label("CharacterPageInfo", (characterPage + 1) + " / " + pages + "   ·   " + filtered.Count + " 名",
                195, 576, 250, 29, 18, T.muted).alignment = TextAlignmentOptions.Center;
            var next = Button("CharacterNextPage", "下一页", 460, 568, 150, 44,
                () => { characterPage++; RefreshBackgroundPicker(); });
            next.interactable = characterPage + 1 < pages;
        }

        static void CharacterThumbnail(Transform parent, LobbyArtwork art)
        {
            if (art.character == null) return;
            Vector2 pixels = new Vector2(art.character.width * Mathf.Abs(art.characterUV.width),
                art.character.height * Mathf.Abs(art.characterUV.height));
            float scale = Mathf.Min(172 / Mathf.Max(1, pixels.x), 104 / Mathf.Max(1, pixels.y));
            Vector2 size = pixels * scale;
            var preview = LobbyUI.Rect(parent, "CharacterPreview", (184 - size.x) / 2,
                5 + (104 - size.y) / 2, size.x, size.y).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            preview.texture = art.character; preview.uvRect = art.characterUV;
            preview.raycastTarget = false;
        }
    }
}
