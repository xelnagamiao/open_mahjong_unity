using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainPanel : MonoBehaviour
{
    [SerializeField] private Button chineseButton;
    [SerializeField] private Button RecordButton;
    [SerializeField] private Button playerButton;

    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private Image profileImage;


    public static MainPanel Instance { get; private set; }
    private void Awake(){
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        chineseButton.onClick.AddListener(ChineseRoom);
        RecordButton.onClick.AddListener(Record);
        playerButton.onClick.AddListener(PlayerInfo);
    }
    private void ChineseRoom(){
        WindowsManager.Instance.SwitchWindow("roomList");
        RoomListPanel.Instance.RefreshRoomList(); // 刷新房间列表
    }

    private void Record(){
        WindowsManager.Instance.SwitchWindow("record");
        NetworkManager.Instance.GetRecordList();
    }

    private void PlayerInfo(){
        WindowsManager.Instance.OpenPlayerInfoPanel();
    }

    public void ShowUserSettings(UserSettings userSettings){
        usernameText.text = userSettings.username;
        Debug.Log($"image/Profiles/{userSettings.profile_image_id}");
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{userSettings.profile_image_id}");
        profileImage.gameObject.GetComponent<ProfileOnClick>().user_id = userSettings.user_id;
    }
}
