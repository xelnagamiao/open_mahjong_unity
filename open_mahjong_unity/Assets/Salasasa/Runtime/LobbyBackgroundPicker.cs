using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    public sealed partial class LobbyOverlay
    {
        const int BackgroundsPerPage = 6;
        static readonly string[] BackgroundCategories = { "全部", "餐饮", "校园", "和风", "街景", "室内", "原有" };
        int artworkTab;
        int backgroundPage;
        string backgroundCategory = "全部";
        readonly List<(UnityEngine.UI.Button button, System.Func<bool> selected)> artworkChoices = new List<(UnityEngine.UI.Button, System.Func<bool>)>();
        TMP_Text combinationLabel, characterStepLabel, backgroundStepLabel, expressionStepLabel;
        UnityEngine.UI.Button previousExpression, nextExpression;

        void ResetArtworkPicker()
        {
            artworkChoices.Clear();
            combinationLabel = characterStepLabel = backgroundStepLabel = expressionStepLabel = null;
            previousExpression = nextExpression = null;
        }

        void TrackArtworkChoice(UnityEngine.UI.Button button, System.Func<bool> selected)
            => artworkChoices.Add((button, selected));

        public void RefreshArtworkSelection()
        {
            if (!IsOpen || combinationLabel == null) return;
            combinationLabel.text = owner.CurrentBackgroundArtwork.sceneName + "  /  " + owner.CurrentArtwork.characterName;
            characterStepLabel.text = "角色  " + (owner.selectedArtworkIndex + 1) + "/" + owner.CharacterCount;
            backgroundStepLabel.text = "背景  " + (owner.selectedBackgroundIndex + 1) + "/" + owner.BackgroundCount;
            expressionStepLabel.text = owner.ExpressionCount > 1 ? "表情 · " + owner.ExpressionName + "  " + (owner.CurrentExpressionIndex + 1) + "/" + owner.ExpressionCount : "表情 · 仅默认";
            previousExpression.interactable = nextExpression.interactable = owner.ExpressionCount > 1;
            foreach (var choice in artworkChoices)
            {
                bool selected = choice.selected();
                var shape = (LobbyShape)choice.button.targetGraphic;
                shape.color = selected ? T.accent : Color.Lerp(T.surface, T.line, .12f);
                shape.outlineColor = selected ? Color.clear : T.line;
                shape.SetVerticesDirty();
                foreach (var text in choice.button.GetComponentsInChildren<TMP_Text>()) text.color = selected ? T.accentInk : T.ink;
            }
        }

        public void RefreshBackgroundPicker()
        {
            if (IsOpen && owner.independentArtworkSelection && (CurrentPage == "character" || CurrentPage == "appearance"))
                Open(CurrentPage, "场景配置");
        }

        void ArtworkPicker()
        {
            Window("场景配置", 640, 720, dimBackground: false);
            Button("BackgroundsTab", "背景  " + owner.BackgroundCount, 30, 132, 184, 40,
                () => { artworkTab = 0; RefreshBackgroundPicker(); }, artworkTab == 0);
            Button("CharactersTab", "角色  " + owner.CharacterCount, 228, 132, 184, 40,
                () => { artworkTab = 1; RefreshBackgroundPicker(); }, artworkTab == 1);
            Button("ArtworkPairsTab", "原有搭配", 426, 132, 184, 40,
                () => { artworkTab = 2; RefreshBackgroundPicker(); }, artworkTab == 2);
            combinationLabel = Label("CurrentCombination", "",
                32, 182, 575, 28, 17, T.muted);

            if (artworkTab == 2) ArtworkPairs();
            else if (artworkTab == 1) CharacterGallery();
            else BackgroundGallery();

            characterStepLabel = Label("CharacterStep", "", 30, 622, 184, 25, 17, T.muted);
            backgroundStepLabel = Label("BackgroundStep", "", 228, 622, 184, 25, 17, T.muted);
            expressionStepLabel = Label("ExpressionStep", "", 426, 622, 184, 25, 15, T.muted);
            Button("PreviousCharacter", "上一位", 30, 654, 88, 44, () => owner.CycleCharacter(-1));
            Button("CycleCharacter", "下一位", 126, 654, 88, 44, () => owner.CycleCharacter(1));
            Button("PreviousBackground", "上一张", 228, 654, 88, 44, () => owner.CycleBackground(-1));
            Button("CycleBackground", "下一张", 324, 654, 88, 44, () => owner.CycleBackground(1));
            previousExpression = Button("PreviousExpression", "上一个", 426, 654, 88, 44, () => owner.CycleExpression(-1));
            nextExpression = Button("CycleExpression", "下一个", 522, 654, 88, 44, () => owner.CycleExpression(1));
            RefreshArtworkSelection();
        }

        void BackgroundGallery()
        {
            var filtered = new List<int>();
            for (int i = 0; i < owner.BackgroundCount; i++)
                if (backgroundCategory == "全部" || owner.BackgroundAt(i).category == backgroundCategory)
                    filtered.Add(i);
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)BackgroundsPerPage));
            backgroundPage = Mathf.Clamp(backgroundPage, 0, pageCount - 1);
            for (int i = 0; i < BackgroundCategories.Length; i++)
            {
                string category = BackgroundCategories[i];
                Button("BackgroundCategory" + i, category, 30 + i * 84, 218, 76, 34,
                    () => { backgroundCategory = category; backgroundPage = 0; RefreshBackgroundPicker(); },
                    backgroundCategory == category);
            }
            int start = backgroundPage * BackgroundsPerPage;
            for (int position = 0; position < BackgroundsPerPage && start + position < filtered.Count; position++)
            {
                int index = filtered[start + position];
                var art = owner.BackgroundAt(index);
                bool selected = owner.selectedBackgroundIndex == index;
                var card = Button("BackgroundChoice" + index, "", 30 + position % 3 * 198,
                    268 + position / 3 * 144, 184, 132, () => owner.SelectBackground(index), selected);
                TrackArtworkChoice(card, () => owner.selectedBackgroundIndex == index);
                BackgroundThumbnail(card.transform, art.background, 5, 5, 174, 87);
                Color ink = selected ? T.accentInk : T.ink;
                LobbyUI.Text(card.transform, "Name", string.IsNullOrEmpty(art.collection) ? art.sceneName : art.collection,
                    9, 95, 166, 20, 17, ink, T.font);
                LobbyUI.Text(card.transform, "Variant", string.IsNullOrEmpty(art.variant) ? "原有场景" : art.variant,
                    9, 115, 166, 16, 12, ink, T.font);
            }
            var previous = Button("BackgroundPreviousPage", "上一页", 30, 568, 150, 44,
                () => { backgroundPage--; RefreshBackgroundPicker(); });
            previous.interactable = backgroundPage > 0;
            Label("BackgroundPageInfo", (backgroundPage + 1) + " / " + pageCount + "   ·   " + filtered.Count + " 张",
                195, 576, 250, 29, 18, T.muted).alignment = TextAlignmentOptions.Center;
            var next = Button("BackgroundNextPage", "下一页", 460, 568, 150, 44,
                () => { backgroundPage++; RefreshBackgroundPicker(); });
            next.interactable = backgroundPage + 1 < pageCount;
        }

        void ArtworkPairs()
        {
            for (int i = 0; i < owner.ArtworkCount; i++)
            {
                int index = i;
                var art = owner.artworkSets[i];
                bool selected = owner.CurrentArtwork.character == art.character && owner.CurrentBackgroundArtwork.background == art.background;
                var row = Button("ArtworkPair" + i, "", 30, 218 + i * 76, 580, 68,
                    () => owner.SelectArtworkPair(index), selected);
                TrackArtworkChoice(row, () => owner.CurrentArtwork.character == art.character && owner.CurrentBackgroundArtwork.background == art.background);
                BackgroundThumbnail(row.transform, art.background, 5, 5, 108, 58);
                Color ink = selected ? T.accentInk : T.ink;
                LobbyUI.Text(row.transform, "Scene", art.sceneName, 129, 7, 380, 28, 22, ink, T.font);
                LobbyUI.Text(row.transform, "Character", art.characterName, 130, 41, 380, 20, 14, ink, T.font);
                LobbyUI.Text(row.transform, "Shortcut", (i + 1).ToString(), 525, 12, 40, 42, 22, ink, T.font, TextAlignmentOptions.Center);
            }
        }

        static void BackgroundThumbnail(Transform parent, Texture2D texture, float x, float y, float width, float height)
        {
            var thumb = LobbyUI.Rect(parent, "BackgroundPreview", x, y, width, height).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            thumb.texture = texture;
            thumb.raycastTarget = false;
            if (texture == null) return;
            float uvHeight = ((float)texture.width / texture.height) / (width / height);
            thumb.uvRect = uvHeight < 1 ? new Rect(0, (1 - uvHeight) / 2, 1, uvHeight) : new Rect((1 - 1 / uvHeight) / 2, 0, 1 / uvHeight, 1);
        }
    }
}
