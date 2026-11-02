using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DevelopmentContentUiBaker
{
    public static void Bake(Scene scene)
    {
        foreach (var owner in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CompleteLobbyController>(true)))
        {
            var visibility = owner.GetComponent<HomeUiVisibility>() ?? owner.gameObject.AddComponent<HomeUiVisibility>();
            visibility.artwork = owner.artwork;
            var access = owner.GetComponent<HomeArtworkAccess>() ?? owner.gameObject.AddComponent<HomeArtworkAccess>();
            access.configurationButtons = owner.artwork.pages
                .Select(page => page.transform.Find("MetroUtility/CityAppearance"))
                .Where(item => item).Select(item => item.gameObject).ToArray();
            access.visibilityButtons = owner.artwork.pages
                .Select(page => page.transform.Find("MetroUtility/CityCharacterVisibility") as RectTransform)
                .Where(item => item).ToArray();
            foreach (var button in access.configurationButtons) button.SetActive(true);
            foreach (var button in access.visibilityButtons)
            {
                button.anchoredPosition = new Vector2(60, button.anchoredPosition.y);
                var action = button.GetComponent<LobbyAction>();
                if (action) { action.action = "silhouette"; action.label = "隐藏界面"; EditorUtility.SetDirty(action); }
            }
            EditorUtility.SetDirty(visibility); EditorUtility.SetDirty(access);
        }
    }
}
