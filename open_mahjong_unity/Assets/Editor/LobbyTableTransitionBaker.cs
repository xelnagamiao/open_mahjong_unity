#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LobbyTableTransitionBaker
{
    public static void Bake(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before baking.");
        var home = Find<CompleteLobbyController>(scene);
        var windows = Find<WindowsManager>(scene);
        if (!home.sourceMenu.GetComponent<CanvasGroup>()) home.sourceMenu.AddComponent<CanvasGroup>();
        var windowData = new SerializedObject(windows);
        var transition = windows.GetComponent<LobbyTableTransition>();
        if (!transition) transition = windows.gameObject.AddComponent<LobbyTableTransition>();
        var lobby = OuterRoots(home.pageRoot.gameObject, home.artwork.gameObject,
            home.headerNavigation.gameObject, home.lobbyOverlays);
        var table = OuterRoots(home.gameCanvas.gameObject, home.boardCanvas.gameObject,
            windowData.FindProperty("tableOverlays").objectReferenceValue as GameObject,
            windowData.FindProperty("recordOverlays").objectReferenceValue as GameObject);
        var data = new SerializedObject(transition);
        SetLayers(data.FindProperty("lobbyLayers"), lobby);
        SetLayers(data.FindProperty("tableLayers"), table);
        data.ApplyModifiedPropertiesWithoutUndo();
        windowData.FindProperty("lobbyTableTransition").objectReferenceValue = transition;
        windowData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(transition);EditorUtility.SetDirty(windows);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static T Find<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();

    static GameObject[] OuterRoots(params GameObject[] candidates)
    {
        var roots = candidates.Where(x => x).Distinct().ToArray();
        return roots.Where(x => !roots.Any(parent => parent != x && x.transform.IsChildOf(parent.transform))).ToArray();
    }

    static void SetLayers(SerializedProperty property, GameObject[] objects)
    {
        property.arraySize = objects.Length;
        for (int i = 0; i < objects.Length; i++)
        {
            var group = objects[i].GetComponent<CanvasGroup>();
            if (!group) group = objects[i].AddComponent<CanvasGroup>();
            property.GetArrayElementAtIndex(i).objectReferenceValue = group;
            EditorUtility.SetDirty(group);
        }
    }

    public static object Validate(Scene scene)
    {
        var home = Find<CompleteLobbyController>(scene);
        var windows = Find<WindowsManager>(scene);
        var windowData = new SerializedObject(windows);
        var transition = windowData.FindProperty("lobbyTableTransition").objectReferenceValue as LobbyTableTransition;
        if (!transition || transition.gameObject != windows.gameObject) throw new InvalidOperationException("The resume transition must remain on WindowsManager.");
        var data = new SerializedObject(transition);
        CanvasGroup[] Read(string key)
        {
            var values = data.FindProperty(key);
            var groups = new CanvasGroup[values.arraySize];
            for (int i = 0; i < groups.Length; i++) groups[i] = values.GetArrayElementAtIndex(i).objectReferenceValue as CanvasGroup;
            if (groups.Length == 0 || groups.Any(x => !x)) throw new InvalidOperationException("Missing baked transition canvas: " + key);
            return groups;
        }
        var lobby = Read("lobbyLayers");var table = Read("tableLayers");
        bool Covered(CanvasGroup[] groups, Transform target) => groups.Any(group => target == group.transform || target.IsChildOf(group.transform));
        foreach (var target in new[] { home.pageRoot, home.bodyLayer.transform, home.artwork.transform, home.headerNavigation.canvas.transform })
            if (!Covered(lobby, target)) throw new InvalidOperationException("The lobby transition misses " + target.name);
        foreach (var target in new[] { home.gameCanvas.transform, home.boardCanvas.transform })
            if (!Covered(table, target)) throw new InvalidOperationException("The table transition misses " + target.name);
        if (lobby.Any(left => table.Any(right => left.transform == right.transform || left.transform.IsChildOf(right.transform) || right.transform.IsChildOf(left.transform))))
            throw new InvalidOperationException("Lobby and table transition roots must remain separate.");
        foreach (var group in lobby.Concat(table))
        {
            if (transition.transform == group.transform || transition.transform.IsChildOf(group.transform))
                throw new InvalidOperationException("The transition owner cannot fade its own parent.");
            if (group.GetComponentsInChildren<CanvasGroup>(true).Any(child => child.ignoreParentGroups))
                throw new InvalidOperationException("A nested canvas ignores the transition root: " + group.name);
        }
        return new { lobby = lobby.Select(x => x.name).ToArray(), table = table.Select(x => x.name).ToArray(), durationPerPhase = windows.WindowFadeDuration };
    }
}
#endif
