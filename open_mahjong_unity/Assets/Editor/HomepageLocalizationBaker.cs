#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bakes presentation bindings into the platform's actual pages and list templates.</summary>
public static class HomepageLocalizationBaker
{
    static readonly Dictionary<string,string[]> UserFields=new Dictionary<string,string[]>{
        {"UserContainer",new[]{"usernameText","titleText"}},
        {"RoomProfileSummary",new[]{"labels"}},
        {"CompleteLobbyController",new[]{"matchCounts"}},
        {"LobbyArtCredits",new[]{"characterLabel","backgroundLabel"}},
        {"SettingsPagePresentation",new[]{"helpText"}},
        {"PlayerInfoPanel",new[]{"usernameText","useridText","titleText"}},
        {"FriendItem",new[]{"uidText","usernameText"}},
        {"FollowingItem",new[]{"uidText","usernameText"}},
        {"FriendRequestItem",new[]{"uidText","usernameText"}},
        {"RoomItem",new[]{"roomName","playerNames"}},
        {"RecordPrefab",new[]{"RecordIdText","Username1Text","Username2Text","Username3Text","Username4Text"}},
        {"RecentMatchCard",new[]{"players"}},
        {"CommunityCard",new[]{"title","description","contact","tag"}},
        {"VenueCardView",new[]{"title","description","identifier"}},
        {"ActivityItem",new[]{"titleText","descText"}},
        {"ActivityDetailPanel",new[]{"titleText","bodyText","summaryText"}},
        {"ActivityBakedBlock",new[]{"text"}},
        {"LeaderboardController",new[]{"playerName","playerUid"}},
        {"EventVenueBinding",new[]{"label"}},
        {"CommunityQrDialog",new[]{"title","caption"}},
        {"ChatMessageRow",new[]{"author","body","time"}},
        {"ChatListRow",new[]{"title","detail","initials"}},
        {"ChatView",new[]{"title"}},
        {"GraphiteChatBridge",new[]{"popupTitle","popupDetail","avatarInitials"}},
        {"TableChatLauncher",new[]{"previewBody"}},
        {"LeaderboardView",new[]{"myName"}},
        {"MailPanel",new[]{"subject","body","sender","date"}},
        {"MailListItem",new[]{"title","date"}},
        {"MailRewardItem",new[]{"title"}},
    };
    public static void Bake(Scene scene)
    {
        PlatformFontFallbackBaker.Bake();
        var roots=scene.GetRootGameObjects().Where(r=>r.name=="MainCanvas"||r.name=="OverlayCanvas").ToArray();
        BakeHierarchy(roots);
    }
    public static void BakePrefabs(Scene scene)
    {
        var components=new HashSet<string>{"ConfigItem","RoomItem","SpectatorPrefab","RecordPrefab","FriendItem","FriendRequestItem","FollowingItem","ActivityItem","MessagePrefab","TipItem","PlayerInfoPanel","PlayerInfoEntry","PlayerInfoStatField"};
        var paths=AssetDatabase.GetDependencies(scene.path,true).Where(p=>p.EndsWith(".prefab"))
            .Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Resources/UI"}).Select(AssetDatabase.GUIDToAssetPath)).Distinct();
        foreach(string path in paths){
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!asset)continue;
            bool selected=path.StartsWith("Assets/Resources/UI/Description/")||asset.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c&&components.Contains(c.GetType().Name));
            if(!selected)continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try{BakeHierarchy(new[]{root});PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
    static void BakeHierarchy(GameObject[] roots)
    {
        var texts=roots.SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)).Distinct().ToArray();
        var excluded=new HashSet<TMP_Text>();
        foreach(var input in roots.SelectMany(r=>r.GetComponentsInChildren<TMP_InputField>(true)))excluded.Add(input.textComponent);
        foreach(var component in roots.SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c)){
            if(component is Salasasa.Leaderboards.LeaderboardView leaderboard){
                foreach(var row in leaderboard.rows.Concat(leaderboard.featured)){if(row!=null){excluded.Add(row.username);excluded.Add(row.uid);}}
            }
            if(!UserFields.TryGetValue(component.GetType().Name,out var fields))continue;
            var serialized=new SerializedObject(component);
            foreach(string field in fields){var property=serialized.FindProperty(field);if(property==null)continue;
                if(property.isArray){for(int i=0;i<property.arraySize;i++)Add(property.GetArrayElementAtIndex(i),excluded);}
                else Add(property,excluded);
            }
        }
        foreach(var label in texts){
            string path=Path(label.transform);
            bool user=excluded.Contains(label)||IsUserPath(path,label.name)||label.GetComponentInParent<LanguageSelector>(true)
                ||label.name=="LanguageCaption"&&label.GetComponentInParent<LoginPanel>(true);
            var existing=label.GetComponent<LocalizedPlatformLabel>();
            if(user){if(existing)Object.DestroyImmediate(existing);continue;}
            if(label.GetComponentInParent<Salasasa.Home.HeaderButtonPresentation>()||label.GetComponentInParent<Salasasa.Home.HeaderReturnGame>())continue;
            var binding=existing?existing:label.gameObject.AddComponent<LocalizedPlatformLabel>();
            if(!existing)binding.key=label.text;
            if(!binding.typographyBaked){binding.authoredAutoSize=label.enableAutoSizing;binding.authoredSize=label.enableAutoSizing?label.fontSizeMax:label.fontSize;binding.minimumSize=Mathf.Min(binding.authoredSize,Mathf.Max(12,binding.authoredSize*.7f));binding.typographyBaked=true;}
            binding.followSourceChanges=true;binding.fitTranslation=true;
            // Rich descriptions keep their wrapping and content-sized layout; only bounded labels shrink.
            if(label.GetComponent<UnityEngine.UI.ContentSizeFitter>()||label.rectTransform.rect.height>100)binding.fitTranslation=false;
            EditorUtility.SetDirty(binding);
        }
        var excludedLegacy=new HashSet<UnityEngine.UI.Text>();
        foreach(var input in roots.SelectMany(r=>r.GetComponentsInChildren<UnityEngine.UI.InputField>(true)))excludedLegacy.Add(input.textComponent);
        foreach(var label in roots.SelectMany(r=>r.GetComponentsInChildren<UnityEngine.UI.Text>(true))){
            var existing=label.GetComponent<LocalizedPlatformText>();
            if(excludedLegacy.Contains(label)||IsUserPath(Path(label.transform),label.name)){if(existing)Object.DestroyImmediate(existing);continue;}
            var binding=existing?existing:label.gameObject.AddComponent<LocalizedPlatformText>();
            if(!existing){binding.key=label.text;binding.authoredSize=label.fontSize;binding.minimumSize=Mathf.Max(12,Mathf.RoundToInt(label.fontSize*.7f));binding.authoredBestFit=label.resizeTextForBestFit;}
            binding.fitTranslation=label.rectTransform.rect.height<=100&&!label.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            EditorUtility.SetDirty(binding);
        }
    }
    static void Add(SerializedProperty property,HashSet<TMP_Text> excluded){if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue is TMP_Text text)excluded.Add(text);}
    static bool IsUserPath(string path,string name)
    {
        string lower=name.ToLowerInvariant();
        if(lower.Contains("username")||lower=="playername"||lower=="roomname"||lower=="userid"||lower=="uid"||lower=="nickname")return true;
        if(path.Contains("/Messages/")||path.Contains("/MessageList/")||path.Contains("/ChatContent/")||path.Contains("/ChatPreview/")||path.Contains("/TableChat/"))return true;
        if(path.Contains("/Console")||path.Contains("/LogText")||path.Contains("/Markdown"))return true;
        if(path.Contains("/Contributors/")||path.Contains("/Acknowledgement")||path.Contains("/ThanksList/")||path.Contains("/ThanksContent/"))return true;
        if(path.Contains("/SupersededModalControls/")||path.Contains("/TableOverlay/RecordOverlay/InfoViewContainer/"))return true;
        return false;
    }
    static string Path(Transform transform){string path=transform.name;while(transform.parent){transform=transform.parent;path=transform.name+"/"+path;}return path;}
    public static string[] Missing(Scene scene)=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LocalizedPlatformLabel>(true))
        .Where(t=>PlatformText.HasChinese(t.key)&&PlatformText.HasChinese(PlatformText.Get(t.key,AppLanguage.English))).Select(t=>t.key).Distinct().ToArray();
}
#endif
