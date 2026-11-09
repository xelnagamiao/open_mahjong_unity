#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SalaCoinShopUiBaker {
    public static void Bake(Scene scene) {
        var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        if (!owner.shop) ShopUiBaker.Bake(scene);
        foreach (var page in owner.artwork.pages) {
            var dock = page.transform.Find("MetroBottomNavigation");
            if (!dock) continue;
            var source = dock.GetComponentsInChildren<LobbyAction>(true).Single(a => a.action == "leaderboard");
            var shop = dock.GetComponentsInChildren<LobbyAction>(true).FirstOrDefault(a => a.action == "shop");
            if (!shop) {
                shop = UnityEngine.Object.Instantiate(source, dock, false); shop.name = "ShopButton";
                shop.action = "shop"; shop.label = "商店";
                shop.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                foreach (var icon in shop.GetComponentsInChildren<LobbyIconBinding>(true)) UnityEngine.Object.DestroyImmediate(icon.gameObject);
                var oldGlyph = shop.transform.Find("DockIcon"); if (oldGlyph) UnityEngine.Object.DestroyImmediate(oldGlyph.gameObject);
            }
            var ir = FinalSettlementUiBaker.Rect("ShopIcon", shop.transform);
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(.5f, 1); ir.sizeDelta = new Vector2(56, 56); ir.anchoredPosition = new Vector2(0, -2);
            var image = ir.GetComponent<Image>() ?? ir.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Currency/shop.png"); image.preserveAspect = true; image.color = Color.white; image.raycastTarget = false;
            var label = shop.transform.Find("DockLabel").GetComponent<TMP_Text>(); FinalSettlementUiBaker.SetLabel(label, "商店");
            var layout = dock.GetComponent<LobbyDockLayout>();
            string[] routes = { "inventory", "shop", "tutorial", "chat", "profile", "leaderboard" };
            var actions = dock.GetComponentsInChildren<LobbyAction>(true);
            layout.buttons = routes.Select(route => (RectTransform)actions.Single(a => a.action == route).transform).ToArray();
            layout.captions = layout.buttons.Select(b => b.Find("DockLabel").GetComponent<TMP_Text>()).ToArray();
            foreach (var rect in layout.buttons) {
                rect.SetAsLastSibling();
                var button = rect.GetComponent<UnityEngine.UI.Button>();
                button.transition = UnityEngine.UI.Selectable.Transition.None;
                var colors = button.colors; colors.selectedColor = colors.normalColor; button.colors = colors;
            }
            layout.minimumWidth = 100; layout.spacing = 12;
            layout.Apply(); EditorUtility.SetDirty(layout);
        }
        var backdrop = owner.shop.backdrop;
        if (!backdrop) throw new InvalidOperationException("Shop backdrop reference is missing.");
        backdrop.interactable = false;
        if (backdrop.targetGraphic) { backdrop.targetGraphic.color = Color.clear; backdrop.targetGraphic.raycastTarget = false; }
        backdrop.gameObject.SetActive(false);
        EditorUtility.SetDirty(backdrop);
        if (backdrop.targetGraphic) EditorUtility.SetDirty(backdrop.targetGraphic);

        var alignment = owner.bodyLayer.GetComponentInChildren<EntranceDockAlignment>(true);
        if (alignment) {
            alignment.firstButtons = owner.artwork.pages.Select(p => (RectTransform)p.transform.Find("MetroBottomNavigation/ShopButton")).ToArray();
            alignment.lastButtons = owner.artwork.pages.Select(p => (RectTransform)p.transform.Find("MetroBottomNavigation/CityDockleaderboard")).ToArray();
            EditorUtility.SetDirty(alignment);
        }
        var plans = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var pageRoot = plans.pages[0].transform;
        float top = pageRoot.Cast<RectTransform>().Where(t => t.gameObject.activeSelf && t.name != "SalaCoin").Select(t => -t.anchoredPosition.y + t.sizeDelta.y).DefaultIfEmpty(0).Max();
        var group = plans.choices.FirstOrDefault(g => g.option == DesignOption.SalaCoin);
        if (group == null) {
            var row = FinalSettlementUiBaker.Rect("SalaCoin", pageRoot); FinalSettlementUiBaker.Top(row, 24, top + 4, 552, 104);
            var sourceTitle = pageRoot.Find("EntrancePalette/Title").GetComponent<TMP_Text>();
            var title = UnityEngine.Object.Instantiate(sourceTitle, row, false); title.name = "Title";
            FinalSettlementUiBaker.Top(title.rectTransform, 0, 2, 552, 36); FinalSettlementUiBaker.SetLabel(title, "萨拉币图标");
            var sourceButton = plans.choices.First(g => g.buttons.Length > 0).buttons[0];
            group = new DesignChoiceGroup { option = DesignOption.SalaCoin, values = new[] { 0, 1, 2, 3 }, buttons = new Button[4] };
            for (int i = 0; i < 4; i++) {
                var button = UnityEngine.Object.Instantiate(sourceButton, row, false); button.name = "Style" + i;
                FinalSettlementUiBaker.Top((RectTransform)button.transform, i * 138, 42, 132, 52);
                button.onClick = new Button.ButtonClickedEvent(); UnityEventTools.AddIntPersistentListener(button.onClick, plans.SelectSalaCoin, i);
                var caption = button.GetComponentInChildren<TMP_Text>(true); FinalSettlementUiBaker.Top(caption.rectTransform, 43, 0, 85, 52);
                caption.fontSize = 16; caption.fontSizeMin = 13; caption.fontSizeMax = 16; caption.enableAutoSizing = true;
                FinalSettlementUiBaker.SetLabel(caption, SalaCoinAppearance.Names[i]);
                var iconRect = FinalSettlementUiBaker.Rect("Coin", button.transform); FinalSettlementUiBaker.Top(iconRect, 5, 10, 32, 32);
                var image = iconRect.gameObject.AddComponent<Image>(); image.sprite = SalaCoinAppearance.SpriteAt(i); image.preserveAspect = true; image.raycastTarget = false;
                group.buttons[i] = button;
            }
            plans.choices = plans.choices.Concat(new[] { group }).ToArray();
        }
        FinalSettlementUiBaker.Top((RectTransform)pageRoot.Find("SalaCoin"), 24, top + 4, 552, 104);
        plans.pageHeights[0] = top + 4 + 104 + 128;
        plans.presetDefaults = InterfacePlansController.RestorableDesignOptions.Select(option => new DesignPresetChoice { option = option, value = plans.DefaultDesignValue(option) }).ToArray();
        foreach (var label in owner.shop.GetComponentsInChildren<LocalizedPlatformLabel>(true).Concat(owner.developerOptions.GetComponentsInChildren<LocalizedPlatformLabel>(true))) {
            if (string.IsNullOrEmpty(label.key) || !label.key.Contains("货币")) continue;
            label.SetSource(label.key.Replace("对局货币", "萨拉币").Replace("货币", "萨拉币")); EditorUtility.SetDirty(label);
        }
        var rule = owner.shop.transform.Find("ShopWindow/RewardRule").GetComponent<LocalizedPlatformLabel>();
        if (rule) rule.SetSource(ShopPanel.RewardRuleText);
        GameEndRewardsUiBaker.BakeShop(owner);
        EditorUtility.SetDirty(plans); EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void Validate(Scene scene) {
        var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        foreach (var page in owner.artwork.pages) {
            var dock = page.transform.Find("MetroBottomNavigation");
            if (!dock) continue;
            var shop = dock.GetComponentsInChildren<LobbyAction>(true).Single(a => a.action == "shop");
            if (!shop.transform.Find("ShopIcon").GetComponent<Image>().sprite || !dock.GetComponent<LobbyDockLayout>().buttons.Contains((RectTransform)shop.transform))
                throw new InvalidOperationException("Home shop entry has not been baked.");
        }
        string[] routes = { "inventory", "shop", "tutorial", "chat", "profile", "leaderboard" };
        foreach (var page in owner.artwork.pages) {
            var dock = page.transform.Find("MetroBottomNavigation");
            if (!dock) continue;
            var layout = dock.GetComponent<LobbyDockLayout>();
            if (!layout.buttons.Select(b => b.GetComponent<LobbyAction>().action).SequenceEqual(routes))
                throw new InvalidOperationException("Shop must immediately follow inventory in the dock.");
        }
        if (!owner.shop.backdrop || owner.shop.backdrop.gameObject.activeSelf || owner.shop.backdrop.interactable ||
            (owner.shop.backdrop.targetGraphic && (owner.shop.backdrop.targetGraphic.raycastTarget || owner.shop.backdrop.targetGraphic.color.a > 0)))
            throw new InvalidOperationException("Shop must not dim or block the surrounding lobby.");
        var plans = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var group = plans.choices.Single(g => g.option == DesignOption.SalaCoin);
        if (group.buttons.Length != 4 || !group.values.SequenceEqual(new[] { 0, 1, 2, 3 }) || Enumerable.Range(0, 4).Any(i => !SalaCoinAppearance.SpriteAt(i)))
            throw new InvalidOperationException("Sala Coin styles have not been baked.");
    }
}
#endif
