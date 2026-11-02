#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlatformNavigationBaker
{
    public static void Bake(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var owner=roots.SelectMany(r=>r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        var nav=owner.headerNavigation;
        var dockActions=roots.SelectMany(r=>r.GetComponentsInChildren<LobbyAction>(true))
            .Where(a=>a.name=="MyDataButton"||(a.transform.parent.name=="MetroBottomNavigation"&&a.action=="sceneConfig")).ToArray();
        var workbench=roots.SelectMany(r=>r.GetComponentsInChildren<LobbyIconWorkbench>(true)).Single();
        if(!nav.tabs.Any(t=>t.route=="sceneConfig"))
        {
            var peer=nav.tabs.First(t=>t.route=="friends");
            var tab=UnityEngine.Object.Instantiate(peer,peer.transform.parent);
            tab.name="SceneConfigButton";tab.route="sceneConfig";tab.navigation=nav;
            tab.button.onClick=new Button.ButtonClickedEvent();
            var unread=tab.transform.Find("Unread");
            if(unread)UnityEngine.Object.DestroyImmediate(unread.gameObject);
            var binding=tab.GetComponentInChildren<LobbyIconBinding>(true);
            binding.iconId="scene";
            var sceneEntry=workbench.library.Find("scene");binding.originalPrimary=sceneEntry.original;
            binding.originalAccent=null;binding.originalShadow=null;
            binding.hasAuthoredGeometry=true;
            binding.originalSize=binding.variantSize=new Vector2(44,40);
            binding.originalOffset=binding.variantOffset=new Vector2(0,8);
            binding.originalArtworkOffset=binding.variantArtworkOffset=Vector2.zero;
            binding.primary.sprite=sceneEntry.original;
            if(binding.accent){binding.accent.gameObject.SetActive(true);binding.accent.enabled=false;}
            if(binding.shadow)binding.shadow.sprite=sceneEntry.original;
            var present=tab.GetComponent<HeaderButtonPresentation>();present.button=tab;tab.presentation=present;
            var profile=nav.GetComponent<HeaderPresentation>().profile;var source=profile.Find("friends");
            var entry=UnityEngine.JsonUtility.FromJson<HeaderEntryPresentation>(UnityEngine.JsonUtility.ToJson(source));
            entry.route="sceneConfig";entry.simplifiedChinese="场景设置";entry.traditionalChinese="場景設定";entry.english="Scene";
            entry.minimumWidth=88;entry.maximumWidth=144;entry.captionSize=new Vector2(88,source.captionSize.y);
            entry.iconSize=new Vector2(44,40);entry.iconOffset=new Vector2(0,8);
            profile.entries.Add(entry);EditorUtility.SetDirty(profile);
            var tabs=nav.tabs.ToList();tabs.Insert(tabs.IndexOf(peer),tab);nav.tabs=tabs.ToArray();
        }
        var playerDataFont=PlayerDataIconBaker.EnsureFont();
        foreach(var action in dockActions)
        {
            action.name="MyDataButton";action.action="profile";action.label="个人数据";
            var label=action.transform.Find("DockLabel").GetComponent<TMP_Text>();SetLabel(label,"个人数据");
            PlayerDataIconBaker.Bind(action,playerDataFont);
            EditorUtility.SetDirty(action);
        }
        workbench.bindings=roots.SelectMany(r=>r.GetComponentsInChildren<LobbyIconBinding>(true)).Distinct().ToArray();
        workbench.playerDataIcons=dockActions.Select(a=>a.GetComponentInChildren<PlayerDataIcon>(true)).ToArray();
        foreach(var dock in roots.SelectMany(r=>r.GetComponentsInChildren<LobbyDockLayout>(true)))dock.Apply();
        var spectator=roots.SelectMany(r=>r.GetComponentsInChildren<SpectatorPanel>(true)).Single();
        var serialized=new SerializedObject(spectator);
        foreach(string field in new[]{"roomFilterDropdown","tierFilterDropdown","formatFilterDropdown"})
        {
            var dropdown=(TMP_Dropdown)serialized.FindProperty(field).objectReferenceValue;
            var r=(RectTransform)dropdown.transform;r.offsetMin=new Vector2(0,3);r.offsetMax=new Vector2(0,3);EditorUtility.SetDirty(r);
        }
        EditorUtility.SetDirty(nav);EditorUtility.SetDirty(workbench);
        HeaderPresentationBaker.Bake();
    }
    static void SetLabel(TMP_Text text,string value)
    {
        text.text=value;var localized=text.GetComponent<LocalizedPlatformLabel>();if(localized)localized.key=value;EditorUtility.SetDirty(text);if(localized)EditorUtility.SetDirty(localized);
    }
}
#endif
