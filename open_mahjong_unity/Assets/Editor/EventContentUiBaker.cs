#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EventContentUiBaker {
    public static void Bake(Scene scene) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before baking event pages.");
        var roots = scene.GetRootGameObjects();
        T One<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
        var lobby = One<EventLobbyPanel>();
        var detail = Ref<EventDetailPanel>(lobby, "detailPanel");
        var room = One<RoomWindowsManager>();
        var create = Ref<GameObject>(room, "createRoomPanel").GetComponent<CreatePanel>();
        var previous = Ref<CreatePanel>(detail, "venueCreatePanel");
        var pageRoot = lobby.transform.parent;
        var overlays = roots.Single(r => r.name == "OverlayCanvas").transform.Find("PageOverlays");
        if (!create || !overlays || pageRoot.name != "PageRoot") throw new InvalidOperationException("Event page containers are missing.");
        var host = overlays.Find("VenueCreation") as RectTransform;
        if (!host) host = new GameObject("VenueCreation", typeof(RectTransform)).GetComponent<RectTransform>();
        host.SetParent(overlays, false);
        host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one;
        host.offsetMin = host.offsetMax = Vector2.zero;
        host.gameObject.SetActive(false);
        detail.gameObject.SetActive(false);
        detail.transform.SetParent(pageRoot, false);
        detail.transform.SetSiblingIndex(lobby.transform.GetSiblingIndex() + 1);
        create.gameObject.SetActive(false);
        Set(create, "roomPageParent", create.transform.parent);
        Set(create, "venueOverlayParent", host);
        Set(detail, "venueCreatePanel", create);
        One<Salasasa.Events.EventVenuePanel>().nativeCreatePanel = create;
        EditorUtility.SetDirty(One<Salasasa.Events.EventVenuePanel>());
        var scope = lobby.GetComponent<PageOwnedOverlays>() ?? lobby.gameObject.AddComponent<PageOwnedOverlays>();
        scope.overlays = (scope.overlays ?? Array.Empty<GameObject>())
            .Where(o => o && !o.GetComponent<CreatePanel>())
            .Concat(new[] { detail.gameObject, host.gameObject }).Distinct().ToArray();
        EditorUtility.SetDirty(scope);
        if (previous && previous != create) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        EditorUtility.SetDirty(detail.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }
    public static void Validate(Scene scene) {
        var roots = scene.GetRootGameObjects();
        var lobby = roots.SelectMany(r => r.GetComponentsInChildren<EventLobbyPanel>(true)).Single();
        var detail = Ref<EventDetailPanel>(lobby, "detailPanel");
        var create = Ref<CreatePanel>(detail, "venueCreatePanel");
        var room = roots.SelectMany(r => r.GetComponentsInChildren<RoomWindowsManager>(true)).Single();
        var native = roots.SelectMany(r => r.GetComponentsInChildren<Salasasa.Events.EventVenuePanel>(true)).Single();
        var host = Ref<RectTransform>(create, "venueOverlayParent");
        var scope = lobby.GetComponent<PageOwnedOverlays>();
        if (roots.SelectMany(r => r.GetComponentsInChildren<CreatePanel>(true)).Count() != 1
            || !detail || !create || !host || detail.transform.parent != lobby.transform.parent
            || host.parent.name != "PageOverlays" || !scope
            || Ref<GameObject>(room, "createRoomPanel") != create.gameObject || native.nativeCreatePanel != create
            || Ref<RectTransform>(create, "roomPageParent") != create.transform.parent
            || !scope.overlays.Contains(detail.gameObject) || !scope.overlays.Contains(host.gameObject)
            || scope.overlays.Contains(create.gameObject)
            || detail.gameObject.activeSelf || host.gameObject.activeSelf || create.gameObject.activeSelf)
            throw new InvalidOperationException("Ordinary rooms, venue rooms and venue presets must share one baked creation view.");
    }
    static T Ref<T>(UnityEngine.Object target, string name) where T : UnityEngine.Object
        => new SerializedObject(target).FindProperty(name).objectReferenceValue as T;
    static void Set(UnityEngine.Object target, string name, UnityEngine.Object value) {
        var data = new SerializedObject(target);
        data.FindProperty(name).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
