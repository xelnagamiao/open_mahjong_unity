using UnityEngine;
using UnityEngine.UI;

public class MainPanel : MonoBehaviour
{
    [SerializeField] private Button chineseButton;
    [SerializeField] private RoomListPanel roomListPanel;

    // Start is called before the first frame update
    void Start()
    {
        chineseButton.onClick.AddListener(ChineseRoom);
    }
    private void ChineseRoom(){
        WindowsManager.Instance.SwitchWindow("roomList");
        roomListPanel.RefreshRoomList(); // 刷新房间列表
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
