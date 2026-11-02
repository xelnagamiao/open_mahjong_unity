#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Guards the authored MainScene hierarchy against reintroducing legacy navigation roots.</summary>
public static class MainCanvasPresentationValidation
{
    [MenuItem("Tools/Mahjong/Main Scene/Validate Presentation Hierarchy")]
    public static void ValidateActiveScene() => Validate(SceneManager.GetActiveScene());

    public static void Validate(Scene scene)
    {
        if (scene.path != "Assets/Scenes/MainScene.unity") return;
        var roots = scene.GetRootGameObjects();
        var main = roots.Single(r => r.name == "MainCanvas").GetComponent<Canvas>();
        var components = roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
        var home = components.OfType<CompleteLobbyController>().Single();
        var header = components.OfType<FourSideHeader>().Single();
        var windows = components.OfType<WindowsManager>().Single();

        Require(home.transform.parent == main.transform, "HomePresentation must be under MainCanvas.");
        Require(header.transform == main.transform.Find("HeaderPanel"), "HeaderPanel must own the visible navigation controller.");
        Require(home.headerNavigation == header && header.owner == home, "Header controller references disagree.");
        Require(header.canvas && header.canvas.transform.parent == header.transform, "The visible navigation must be inside HeaderPanel.");
        Require(header.canvas.GetComponentsInParent<Canvas>(true).Last() == main, "Navigation is not using MainCanvas.");
        Require(header.main && header.more && header.tabs.All(tab => tab && tab.button), "Navigation is missing a live button.");
        Require(header.returnGame && header.returnGame.GetComponentInParent<FourSideHeader>(true) == header,
            "Return-to-table must bind to the visible header.");
        Require(new SerializedObject(windows).FindProperty("headerPanel").objectReferenceValue == header.gameObject,
            "WindowsManager is still bound to an obsolete header.");

        var background = home.artwork;
        Require(background && background.transform == main.transform.Find("BackgroundPanel"), "The home artwork must be inside MainCanvas/BackgroundPanel.");
        Require(background.canvas && background.canvas.GetComponentsInParent<Canvas>(true).Last() == main && !background.canvas.overrideSorting,
            "The background must inherit MainCanvas rendering.");
        Require(background.backgroundImage && background.backgroundImage.transform.IsChildOf(background.transform),
            "BackgroundImage is missing from BackgroundPanel.");
        Require(background.characterLayer && background.characterLayer.transform.IsChildOf(background.transform),
            "The character is missing from BackgroundPanel.");
        Require(background.transform.GetSiblingIndex() < home.pageRoot.GetSiblingIndex(), "The background must render before the page content.");
        foreach (var layer in new[] { background.transform, header.canvas.transform })
        {
            var layout = layer.GetComponent<CanvasReferenceLayout>();
            Require(layout && layout.referenceArea == (RectTransform)main.transform && layout.scaleSettings,
                "Authored reference scaling is missing on " + layer.name);
        }

        Require(home.pageRoot.parent == main.transform, "PageRoot must stay inside MainCanvas.");
        Require(home.lobbyOverlays && home.lobbyOverlays.transform.parent.name == "OverlayCanvas",
            "Modal overlays must stay on OverlayCanvas.");
        Require(!roots.Any(r => r.name == "HomePresentation" || r.name == "LobbyCanvas"), "A duplicate standalone presentation root remains.");
        Require(!components.Where(c => c).Any(c => new[] { "HeaderPanel", "HeaderButton", "HeaderDataIconChoices" }.Contains(c.GetType().Name)),
            "A retired navigation component remains.");
        Require(roots.Sum(r => r.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))) == 0,
            "The scene contains a missing script.");
        LobbyTableTransitionBaker.Validate(scene);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
