using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    public sealed class DeveloperOptionsPanel : MonoBehaviour
    {
        public CompleteLobbyController owner;
        public Button closeButton, dismissButton, classroomButton;
        public UnityEngine.UI.Button shopButton;
        public TMPro.TMP_Text walletLabel;
        UserDataManager walletOwner;
        public bool IsOpen => gameObject.activeSelf;
        void OnEnable() {
            walletOwner=UserDataManager.Instance;
            if(walletOwner!=null)walletOwner.OnWalletChanged+=RefreshWalletLabel;
            ConfigManager.OnLanguageChanged+=RefreshWalletLabel;
            RefreshWalletLabel();NetworkManager.Instance?.RefreshWallet();
        }
        void OnDisable() {
            if(walletOwner!=null)walletOwner.OnWalletChanged-=RefreshWalletLabel;
            ConfigManager.OnLanguageChanged-=RefreshWalletLabel;
        }
        void RefreshWalletLabel() {
            if(walletLabel)walletLabel.text=walletOwner?.Wallet==null?PlatformText.Get("持有萨拉币 —"):
                PlatformText.Format("持有萨拉币 {0}",ShopPanel.Money(walletOwner.Wallet.balance_minor));
        }

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
            dismissButton.onClick.AddListener(Close);
            classroomButton.onClick.AddListener(() => owner.Open("classroom"));
            if(shopButton)shopButton.onClick.AddListener(() => owner.Open("shop"));
        }

        public void Show()
        {
            if(CompleteLobbyEnvironment.IsDevelopment)gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        void Update()
        {
            if(!CompleteLobbyEnvironment.IsDevelopment)Close();
        }
    }
}
