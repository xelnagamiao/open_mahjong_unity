using UnityEngine;
using UnityEngine.UI;

public class MainPanel : MonoBehaviour
{
    [SerializeField] private Button chineseButton;

    // Start is called before the first frame update
    void Start()
    {
        chineseButton.onClick.AddListener(ChineseRoom);
    }
    private void ChineseRoom(){
        WindowsManager.Instance.SwitchWindow("roomList");
        RoomListPanel.Instance.RefreshRoomList(); // 刷新房间列表
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
