using UnityEngine;
using UnityEngine.UI;

public class MainPanel : MonoBehaviour
{
    [SerializeField] private Button chineseButton;
    [SerializeField] private Button RecordButton;

    // Start is called before the first frame update
    void Start()
    {
        chineseButton.onClick.AddListener(ChineseRoom);
        RecordButton.onClick.AddListener(Record);
    }
    private void ChineseRoom(){
        WindowsManager.Instance.SwitchWindow("roomList");
        RoomListPanel.Instance.RefreshRoomList(); // 刷新房间列表
    }

    private void Record(){
        WindowsManager.Instance.SwitchWindow("record");
        NetworkManager.Instance.GetRecordList();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
