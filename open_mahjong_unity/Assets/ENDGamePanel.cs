using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ENDGamePanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI rank1username;
    [SerializeField] private TextMeshProUGUI rank1score;
    [SerializeField] private TextMeshProUGUI rank1rank;
    [SerializeField] private TextMeshProUGUI rank1pt;
    [SerializeField] private TextMeshProUGUI rank2username;
    [SerializeField] private TextMeshProUGUI rank2score;
    [SerializeField] private TextMeshProUGUI rank2rank;
    [SerializeField] private TextMeshProUGUI rank2pt;
    [SerializeField] private TextMeshProUGUI rank3username;
    [SerializeField] private TextMeshProUGUI rank3score;
    [SerializeField] private TextMeshProUGUI rank3rank;
    [SerializeField] private TextMeshProUGUI rank3pt;
    [SerializeField] private TextMeshProUGUI rank4username;
    [SerializeField] private TextMeshProUGUI rank4score;
    [SerializeField] private TextMeshProUGUI rank4rank;
    [SerializeField] private TextMeshProUGUI rank4pt;
    [SerializeField] private TextMeshProUGUI gameRandomSeed;
    [SerializeField] private Button goHomeButton;

    // 单例模式
    public static ENDGamePanel Instance { get; private set; }
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }
    public void ShowGameEndPanel(long game_random_seed, Dictionary<int, Dictionary<string, object>> player_final_data)
    {
        this.gameObject.SetActive(true);
        rank1username.text = player_final_data[1]["username"].ToString();
        rank1score.text = player_final_data[1]["score"].ToString();
        rank1rank.text = player_final_data[1]["rank"].ToString();
        rank1pt.text = player_final_data[1]["pt"].ToString();
        rank2username.text = player_final_data[2]["username"].ToString();
        rank2score.text = player_final_data[2]["score"].ToString();
        rank2rank.text = player_final_data[2]["rank"].ToString();
        rank2pt.text = player_final_data[2]["pt"].ToString();
        rank3username.text = player_final_data[3]["username"].ToString();
        rank3score.text = player_final_data[3]["score"].ToString();
        rank3rank.text = player_final_data[3]["rank"].ToString();
        rank3pt.text = player_final_data[3]["pt"].ToString();
        rank4username.text = player_final_data[4]["username"].ToString();
        rank4score.text = player_final_data[4]["score"].ToString();
        rank4rank.text = player_final_data[4]["rank"].ToString();
        rank4pt.text = player_final_data[4]["pt"].ToString();
        gameRandomSeed.text = game_random_seed.ToString();
        goHomeButton.onClick.AddListener(OnGoHomeButtonClick);
    }

    private void OnGoHomeButtonClick()
    {
        this.gameObject.SetActive(false);
        WindowsManager.Instance.SwitchWindow("main");
    }


}
