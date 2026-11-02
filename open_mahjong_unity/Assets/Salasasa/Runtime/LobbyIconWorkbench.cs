using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Salasasa.Home
{
    public sealed class LobbyIconWorkbench : MonoBehaviour
    {
        [Serializable]
        public sealed class Choice
        {
            public string id;
            public int style;
            public UnityEngine.UI.Button button;
            public GameObject selected;
        }

        public CompleteLobbyController owner;
        public InterfacePlansController plans;
        public LobbyIconLibrary library;
        public LobbyIconBinding[] bindings;
        public PlayerDataIcon[] playerDataIcons = Array.Empty<PlayerDataIcon>();
        public PlayerDataIcon[] inventoryIcons = Array.Empty<PlayerDataIcon>();
        public GameObject panel;
        public UnityEngine.UI.Button closeButton, openButton;
        public Choice[] choices;
        public bool IsOpen => plans && plans.gameObject.activeInHierarchy;
        int playerDataStyle = PlayerDataIcon.DefaultStyle;
        int inventoryStyle = 1;
        public const string InventoryIconId = "inventory-symbol";
        public int SelectedPlayerDataStyle => playerDataStyle;
        readonly List<Action> unbind = new List<Action>();
        public const string MailIconId = "mail";
        public const string PlayerDataIconId = "my-data";
        public const string PreferencePrefix = "salasasa.lobby-icons.v2.";
        public static string PreferenceKey(string id) => PreferencePrefix + id;

        void Awake()
        {
            LoadPreferences();
            Listen(closeButton, Close);
            Listen(openButton, Toggle);
            if(panel)panel.SetActive(false);
        }

        void Listen(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction action)
        {
            if(button==null)return;
            button.onClick.AddListener(action);
            unbind.Add(()=>{if(button!=null)button.onClick.RemoveListener(action);});
        }

        void Update()
        {
            var key=Keyboard.current;
            if(key==null || !key.f3Key.wasPressedThisFrame)return;
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            if(selected!=null && selected.GetComponent<TMP_InputField>()!=null)return;
            if(UserDataManager.Instance!=null && UserDataManager.Instance.UserId>0)Toggle();
        }

        public int GetStyle(string id) => id == InventoryIconId ? inventoryStyle : id == PlayerDataIconId ? playerDataStyle
            : id == MailIconId ? LobbyIconBinding.OriginalStyle : library.Find(id).defaultStyle;
        public void LoadPreferences()
        {
            inventoryStyle = 1;
            playerDataStyle = PlayerDataIcon.DefaultStyle;
            RefreshBindings();
        }
        public void SelectPlayerData(int style)
        {
            playerDataStyle = PlayerDataIcon.DefaultStyle;
            foreach (var icon in playerDataIcons) if (icon) icon.Apply(playerDataStyle);
            foreach (var icon in inventoryIcons) if (icon) icon.Apply(inventoryStyle);
            RefreshChoices();
        }
        public void RestoreDefaults()
        {
            SelectInventory(1);
            SelectPlayerData(PlayerDataIcon.DefaultStyle);
            RefreshBindings();
        }

        void RefreshBindings()
        {
            foreach(var binding in bindings)if(binding!=null)binding.Apply(GetStyle(binding.iconId));
            foreach (var icon in playerDataIcons) if (icon) icon.Apply(playerDataStyle);
            foreach (var icon in inventoryIcons) if (icon) icon.Apply(inventoryStyle);
            RefreshChoices();
        }

        public void SelectInventory(int style)
        {
            inventoryStyle=1;
            foreach(var icon in inventoryIcons)if(icon)icon.Apply(inventoryStyle);
            RefreshChoices();
        }

        void RefreshChoices()
        {
            if(choices==null)return;
            foreach(var choice in choices)if(choice.selected!=null)choice.selected.SetActive(GetStyle(choice.id)==choice.style);
        }

        public void Toggle(){if(IsOpen)Close();else Open();}
        public void Open(){owner.headerNavigation.CloseUtility();if(plans){plans.gameObject.SetActive(true);plans.SelectPage(0);}}
        public void Close(){if(plans)plans.Close();}

        void LateUpdate()
        {
            if(IsOpen&&UserDataManager.Instance!=null&&UserDataManager.Instance.UserId<=0)Close();
        }

        void OnDestroy(){foreach(var action in unbind)action();unbind.Clear();}
    }
}
