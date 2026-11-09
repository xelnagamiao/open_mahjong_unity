using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Salasasa.Leaderboards;

namespace Salasasa.Home
{
    public enum DesignOption { Navigation = 1, Home = 2, EntrancePalette = 4, Inventory = 7, History = 9, Settings = 10, EventDesign = 15, SettingsHint = 17, MailPalette = 23, HomeCurtain = 24, RankingPalette = 27, ProfilePalette = 28, SalaCoin = 29 }
    [Serializable] public sealed class DesignChoiceGroup { public DesignOption option; public Button[] buttons; public int[] values; }

    [DisallowMultipleComponent]
    public sealed partial class InterfacePlansController : MonoBehaviour
    {
        public CompleteLobbyController owner;
        public HomeHeaderAppearance home;
        public HomeHeaderPalette homePalette;
        public EntranceMainBridge entrances;
        public InventoryLayoutVariants inventory;
        public PlayerInfoAppearance profile;
        public Salasasa.Venues.VenueContainerLayout venue;
        public SettingsStyleCollection settings;
        public Salasasa.DataLab.HomeDataPanel statistics;
        public LeaderboardController ranking;
        public Salasasa.Events.EventVenuePanel eventVenue;
        public LobbyIconWorkbench iconWorkbench;
        public RectTransform frame;
        public GameObject[] pages;
        public Button[] tabs;
        public float[] pageHeights;
        public DesignChoiceGroup[] choices;
        public int SelectedPage { get; private set; }
        Vector2 measured;
        Vector4 measuredInsets;
        bool positioned;

        void OnEnable() { Refresh(); Fit(); }
        void LateUpdate()
        {
            if (frame && frame.parent is RectTransform parent && (parent.rect.size != measured || (owner && owner.headerNavigation && measuredInsets!=owner.headerNavigation.Insets))) Fit();
        }
        public void SelectPage(int index)
        {
            SelectedPage = Mathf.Clamp(index, 0, pages.Length - 1);
            Refresh(); Fit();
        }
        public void Close() => gameObject.SetActive(false);
        public bool HandleEscape()
        {
            if (!gameObject.activeInHierarchy) return false;
            foreach (var dropdown in GetComponentsInChildren<TMP_Dropdown>())
                if (dropdown.IsExpanded) { dropdown.Hide(); return true; }
            Close(); return true;
        }
        public void SelectNavigation(int value) => Apply(DesignOption.Navigation, value);
        public void SelectHome(int value) => Apply(DesignOption.Home, value);
        public void SelectHomeCurtain(int value) => Apply(DesignOption.HomeCurtain, value);
        public void SelectEntrancePalette(int value) => Apply(DesignOption.EntrancePalette, value);
        public void SelectInventory(int value) => Apply(DesignOption.Inventory, value);
        public void SelectProfilePalette(int value) => Apply(DesignOption.ProfilePalette, value);
        public void SelectRankingPalette(int value) => Apply(DesignOption.RankingPalette, value);
        public void SelectHistory(int value) => Apply(DesignOption.History, value);
        public void SelectSettings(int value) => Apply(DesignOption.Settings, value);
        public void SelectSettingsHint(int value) => Apply(DesignOption.SettingsHint, value);
        public void SelectEventDesign(int value) => Apply(DesignOption.EventDesign, value);
        public void SelectSalaCoin(int value) => Apply(DesignOption.SalaCoin, value);
        public void SelectMailPalette(int value) => Apply(DesignOption.MailPalette, value);
        void Preview(string route)
        {
            if (route != "inventory" && owner.inventory) owner.inventory.Close();
            if (route != "leaderboard" && owner.leaderboard) owner.leaderboard.Close();
            if (route != "mail" && owner.mail) owner.mail.Close();
            if (route == "home") owner.OpenHome();
            else if (route == "leaderboard")
            {
                if (!ranking.IsOpen) { owner.OpenHome(); owner.Open(route); }
            }
            else if (route == "inventory")
            {
                if (!owner.inventory.gameObject.activeSelf) { owner.OpenHome(); owner.Open(route); }
            }
            else if (route == "mail")
            {
                if (!owner.mail.IsOpen) owner.Open(route);
            }
            else if (owner.CurrentPageRoute() != route) owner.Open(route);
            gameObject.SetActive(true);
        }
        void Apply(DesignOption option, int value)
        {
            switch (option)
            {
                case DesignOption.SalaCoin: SalaCoinAppearance.Select(value); break;
                case DesignOption.Navigation: owner.headerNavigation.SelectSide(value); break;
                case DesignOption.Home: Preview("home"); home.SelectVariant(value); break;
                case DesignOption.HomeCurtain: Preview("home"); owner.headerNavigation.SelectHomeCurtain(value); break;
                case DesignOption.EntrancePalette: Preview("home"); entrances.SelectPalette(value); break;
                case DesignOption.Inventory: Preview("inventory"); inventory.SelectVariant(value); break;
                case DesignOption.ProfilePalette: profile.SelectPalette(value); break;
                case DesignOption.RankingPalette: Preview("leaderboard"); ranking.paletteEditor.SelectPreset(value); break;
                case DesignOption.History: Preview("statistics"); statistics.SetCardStyle(value); break;
                case DesignOption.Settings: Preview("settings"); settings.SelectVariant(value); break;
                case DesignOption.SettingsHint: Preview("settings"); settings.SelectHintStyle(value); break;
                case DesignOption.EventDesign: eventVenue.ConfigureAppearance(value, PlayerPrefs.GetInt("EventVenueOriginalPrefabs", 0) == 1); break;
                case DesignOption.MailPalette: Preview("mail"); owner.mail.SelectPalette(value); break;
            }
            gameObject.SetActive(true); Refresh();
        }
        public int Selected(DesignOption option)
        {
            switch (option)
            {
                case DesignOption.SalaCoin: return SalaCoinAppearance.Selected;
                case DesignOption.Navigation: return owner.headerNavigation.Side;
                case DesignOption.Home: return home.Variant;
                case DesignOption.HomeCurtain: return owner.headerNavigation.HomeCurtainEnabled ? 1 : 0;
                case DesignOption.EntrancePalette: return entrances.SelectedPalette;
                case DesignOption.Inventory: return inventory.Variant;
                case DesignOption.ProfilePalette: return profile ? profile.SelectedPalette : 0;
                case DesignOption.RankingPalette: return ranking.paletteEditor.SelectedPreset;
                case DesignOption.History: return statistics.cardStyleIndex;
                case DesignOption.Settings: return settings.Variant;
                case DesignOption.SettingsHint: return settings.HintStyle;
                case DesignOption.EventDesign: return Mathf.Clamp(PlayerPrefs.GetInt("EventVenuePalette", 0), 0, 3);
                case DesignOption.MailPalette: return owner.mail ? owner.mail.PaletteIndex : 0;
                default: return 0;
            }
        }
        public void Refresh()
        {
            if (pages == null || pages.Length == 0) return;
            for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == SelectedPage);
            Mark(tabs, SelectedPage);
            foreach (var row in choices ?? Array.Empty<DesignChoiceGroup>())
            {
                int selected = Array.IndexOf(row.values, Selected(row.option));
                Mark(row.buttons, selected);
            }
        }
        static void Mark(Button[] buttons, int selected)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++) if (buttons[i] && buttons[i].image)
                buttons[i].image.color = i == selected ? new Color32(51, 115, 143, 255) : new Color32(43, 58, 73, 255);
        }
        public void Fit()
        {
            if (!frame || !(frame.parent is RectTransform parent) || pages == null || pages.Length == 0) return;
            float previousTop = frame.anchoredPosition.y + frame.rect.height * frame.localScale.y * .5f;
            measured = parent.rect.size;
            measuredInsets=owner&&owner.headerNavigation?owner.headerNavigation.Insets:Vector4.zero;
            Rect available=AvailableArea(parent);
            float height = pageHeights[SelectedPage];
            frame.sizeDelta = new Vector2(600, height);
            float scale = Mathf.Min(1, available.width / 600f, available.height / height);
            frame.localScale = Vector3.one * Mathf.Max(.1f, scale);
            if (!positioned)
            {
                positioned = true;
                frame.anchoredPosition = new Vector2(available.xMin+300*scale,available.yMax-height*.5f*scale);
            }
            else frame.anchoredPosition = new Vector2(frame.anchoredPosition.x, previousTop - height * scale * .5f);
            ClampPosition();
        }
        public void ClampPosition()
        {
            var parent = (RectTransform)frame.parent;
            var half = frame.sizeDelta * frame.localScale.x * .5f;
            var available=AvailableArea(parent);
            var p = frame.anchoredPosition;
            p.x = Mathf.Clamp(p.x,available.xMin+half.x,available.xMax-half.x);
            p.y = Mathf.Clamp(p.y,available.yMin+half.y,available.yMax-half.y);
            frame.anchoredPosition = p;
        }
        Rect AvailableArea(RectTransform parent)
        {
            var inset=owner&&owner.headerNavigation?owner.headerNavigation.Insets:Vector4.zero;
            float scale=owner&&owner.headerNavigation?CanvasReferenceLayout.EffectiveScale(owner.headerNavigation.canvas)/CanvasReferenceLayout.PixelsPerUnit(parent):1;
            var r=parent.rect;
            return Rect.MinMaxRect(r.xMin+inset.x*scale+16,r.yMin+inset.w*scale+16,r.xMax-inset.z*scale-16,r.yMax-inset.y*scale-16);
        }
    }
}
