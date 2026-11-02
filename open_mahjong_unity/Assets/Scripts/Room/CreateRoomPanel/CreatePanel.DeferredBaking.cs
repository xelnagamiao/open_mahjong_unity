#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private const string DeferredAssetRoot = "Assets/Resources/CreateRoomPanels/";
    public static GameObject DeferredPrefab(string key) => AssetDatabase.LoadAssetAtPath<GameObject>(DeferredAssetRoot + key + ".prefab");

    public void RestoreDeferredPanelsForBaking() {
        foreach (var key in DetailedRules) {
            if (transform.Find("DetailedConfigPanel_" + key)) continue;
            var prefab = DeferredPrefab(key);
            if (!prefab && key == "hongque") { BakeHongqueDetailsPanel(); continue; }
            if (!prefab) throw new InvalidOperationException("Missing authored create-room panel: " + key);
            var instance = Instantiate(prefab, transform, false);
            instance.name = "DetailedConfigPanel_" + key;
            foreach (var help in instance.GetComponentsInChildren<CreateRoomHelpTrigger>(true)) help.BindOwner(this);
        }
        foreach (var key in new[] { "HongKongSettings", "FuzhouSettings" }) {
            if (RoomContent.Find(key)) continue;
            var prefab = DeferredPrefab(key);
            if (prefab) { var instance = Instantiate(prefab, RoomContent, false); instance.name = key; }
        }
        var taiwan = transform.Find("DetailedConfigPanel_taiwan");
        if (!taiwan.Find("FanTablePanel")) {
            var fan = Instantiate(DeferredPrefab("TaiwanFanTable"), taiwan, false);
            fan.name = "FanTablePanel";
            foreach (var help in fan.GetComponentsInChildren<CreateRoomHelpTrigger>(true)) help.BindOwner(this);
        }
    }

    public void SaveDeferredPanels() {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/CreateRoomPanels")) AssetDatabase.CreateFolder("Assets/Resources", "CreateRoomPanels");
        var taiwan = transform.Find("DetailedConfigPanel_taiwan");
        var fan = taiwan ? taiwan.Find("FanTablePanel") : null;
        if (fan) Export("TaiwanFanTable", fan.gameObject);
        foreach (var key in DetailedRules) {
            var view = transform.Find("DetailedConfigPanel_" + key);
            if (view) Export(key, view.gameObject);
        }
        var support = transform.Find("RuleSupportTablePanel");
        if (support) Export("RuleSupportTable", support.gameObject);
        foreach (var key in new[] { "HongKongSettings", "FuzhouSettings" }) {
            var view = RoomContent.Find(key);
            if (view) Export(key, view.gameObject);
        }
        hongKongView = fuzhouView = null;
        ruleSupportPanel = null; ruleSupportCloseButton = null; ruleSupportBackdrop = null;
        ruleSupportScroll = null; ruleSupportHeader = null;
        deferredPanelsBaked = true;
        EditorUtility.SetDirty(this);
    }

    private void Export(string key, GameObject view) {
        view.SetActive(false);
        foreach (var help in view.GetComponentsInChildren<CreateRoomHelpTrigger>(true)) help.BindOwner(null);
        foreach (var component in view.GetComponentsInChildren<Component>(true)) {
            if (!component) throw new InvalidOperationException(view.name + " has a missing script");
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true)) {
                if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue || EditorUtility.IsPersistent(property.objectReferenceValue)) continue;
                var target = property.objectReferenceValue is GameObject obj ? obj.transform
                    : property.objectReferenceValue is Component item ? item.transform : null;
                // Parent transform is rewritten by PrefabUtility.
                if (property.propertyPath == "m_Father") continue;
                if (target && target != view.transform && !target.IsChildOf(view.transform))
                    throw new InvalidOperationException(view.name + "/" + component.name + ": external reference " + property.propertyPath);
            }
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(view, DeferredAssetRoot + key + ".prefab", out bool saved);
        if (!saved || !prefab) throw new InvalidOperationException("Could not save " + key);
        DestroyImmediate(view);
    }

    public void ValidateDeferredPanels() {
        if (!deferredPanelsBaked || transform.Cast<Transform>().Any(t => t.name.StartsWith("DetailedConfigPanel_") || t.name == "RuleSupportTablePanel"))
            throw new InvalidOperationException("Create-room advanced views must be saved as deferred prefabs.");
        foreach (var key in new[] { "HongKongSettings", "FuzhouSettings" })
            if (RoomContent.Find(key) || !DeferredPrefab(key) || DeferredPrefab(key).activeSelf)
                throw new InvalidOperationException("Inline rule settings must be saved as inactive deferred prefabs: " + key);
        foreach (var key in DetailedRules.Concat(new[] { "TaiwanFanTable" })) {
            var prefab = DeferredPrefab(key);
            if (!prefab || prefab.activeSelf || !prefab.GetComponent<CreateRoomPopupMotion>())
                throw new InvalidOperationException("Invalid deferred create-room panel: " + key);
            var scroll = prefab.transform.Find("Dialog/Create_Panel/ScrollArea")?.GetComponent<ScrollRect>();
            var layout = scroll ? scroll.content.GetComponent<CreateRoomDetailsLayout>() : null;
            if (!layout || layout.Columns != CreateRoomDetailsLayout.DefaultColumns || layout.padding.bottom < 72 || scroll.scrollSensitivity < 100)
                throw new InvalidOperationException("Invalid deferred layout: " + key);
        }
    }
}
#endif
