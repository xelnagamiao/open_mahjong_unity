using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SceneConfigPanel : MonoBehaviour
{
    [SerializeField] private TableClothPanel tableClothPanel;
    [SerializeField] private TableEdgePanel tableEdgePanel;
    [SerializeField] private CharacterPanel characterPanel;

    [SerializeField] private Button ShowTableClothPanelButton;
    [SerializeField] private Button ShowTableEdgePanelButton;
    [SerializeField] private Button ShowCharacterPanelButton;

    private void Awake() {
        ShowTableClothPanelButton.onClick.AddListener(ShowTableClothPanel);
        ShowTableEdgePanelButton.onClick.AddListener(ShowTableEdgePanel);
        ShowCharacterPanelButton.onClick.AddListener(ShowCharacterPanel);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);
    }

    private void ShowTableClothPanel() {

        tableClothPanel.gameObject.SetActive(true);
        tableEdgePanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);

        tableClothPanel.LoadTablecloths();
    }
    private void ShowTableEdgePanel() {
        tableEdgePanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        characterPanel.gameObject.SetActive(false);

        tableEdgePanel.LoadTableEdges();
    }
    private void ShowCharacterPanel() {
        characterPanel.gameObject.SetActive(true);
        tableClothPanel.gameObject.SetActive(false);
        tableEdgePanel.gameObject.SetActive(false);
    }
    
    
}
