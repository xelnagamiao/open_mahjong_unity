using UnityEngine;
using UnityEngine.UI;

public class LinksContent : MonoBehaviour
{
    [SerializeField] private Button officialWebsiteButton; // 官方网址按钮
    [SerializeField] private Button githubButton; // GitHub按钮
    [SerializeField] private Button documentButton; // 语雀文档按钮

    private void Awake()
    {
        officialWebsiteButton.onClick.AddListener(OnOfficialWebsiteClick);
        githubButton.onClick.AddListener(OnGithubClick);
        documentButton.onClick.AddListener(OnDocumentClick);
    }

    private void OnOfficialWebsiteClick()
    {
        Application.OpenURL(ConfigManager.webUrl);
    }

    private void OnGithubClick()
    {
        Application.OpenURL(ConfigManager.githubUrl);
    }

    private void OnDocumentClick()
    {
        Application.OpenURL(ConfigManager.documentUrl);
    }
}
