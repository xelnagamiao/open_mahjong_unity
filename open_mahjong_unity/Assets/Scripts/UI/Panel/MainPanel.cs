using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MenuPanel : MonoBehaviour
{


    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private Image profileImage;



    public static MenuPanel Instance { get; private set; }
    private void Awake(){
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    // Start is called before the first frame update


    public void ShowUserSettings(UserSettings userSettings){
        usernameText.text = userSettings.username;
        Debug.Log($"image/Profiles/{userSettings.profile_image_id}");
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{userSettings.profile_image_id}");
        profileImage.gameObject.GetComponent<ProfileOnClick>().user_id = userSettings.user_id;
    }
}
