using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SettingsPagePrefabBaker
{
    static SettingsStyleCollection Collection(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<SettingsStyleCollection>(true)).Single();

    static string PathFor(SettingsBakedPage page) =>
        "Assets/Resources/" + SettingsStyleCollection.PageResource(page.variant, page.category) + ".prefab";

    // The caller owns saving the scene. Write and verify all nine assets before
    // removing the old hierarchy so a failed import leaves its source intact.
    public static void Extract(Scene scene)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before extracting settings pages.");
        var settings = Collection(scene);
        if (settings.UsesPagePrefabs) { Validate(scene); return; }
        var pages = settings.bakedPages;
        ValidatePages(pages);
        var roots = Enumerable.Range(0, 3)
            .Select(variant => pages.Where(p => p.variant == variant).Select(p => p.transform.parent).Distinct().Single()).ToArray();
        foreach (var page in pages)
        {
            string path = PathFor(page);
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var copy = Object.Instantiate(page.gameObject, page.transform.parent, false);
            try
            {
                copy.name = page.name;
                copy.SetActive(false);
                copy.GetComponent<SettingsPagePresentation>().settings = null;
                PrefabUtility.SaveAsPrefabAsset(copy, path, out bool saved);
                if (!saved) throw new InvalidOperationException("Failed to save " + path);
            }
            finally { Object.DestroyImmediate(copy); }
        }
        ValidatePages(pages.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(p)).GetComponent<SettingsBakedPage>()));
        settings.UsePagePrefabs(roots);
        foreach (var page in pages) Object.DestroyImmediate(page.gameObject);
        EditorUtility.SetDirty(settings);
        EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    public static void EditPages(SettingsStyleCollection settings, Func<SettingsBakedPage, bool> select, Action<SettingsBakedPage> change)
    {
        // A menu command can run against an older in-scene bake or the new prefab library.
        foreach (var page in settings.AuthoredPages.Where(select).ToArray())
        {
            if (!settings.UsesPagePrefabs) { change(page); continue; }
            var path = PathFor(page);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                change(root.GetComponent<SettingsBakedPage>());
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new InvalidOperationException("Failed to update " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    public static void Validate(Scene scene)
    {
        var settings = Collection(scene);
        ValidatePages(settings.AuthoredPages);
        if (!settings.UsesPagePrefabs) return;
        if (settings.themeRoots.Any(root => !root || !root.IsChildOf(settings.transform)) ||
            (!Application.isPlaying && settings.GetComponentsInChildren<SettingsBakedPage>(true).Length != 0))
            throw new InvalidOperationException("Settings must have three mount points and no preloaded scene pages.");
    }

    static void ValidatePages(IEnumerable<SettingsBakedPage> source)
    {
        var pages = source.ToArray();
        if (pages.Length != 9) throw new InvalidOperationException("Expected nine authored settings pages.");
        for (int variant = 0; variant < 3; variant++)
            for (int category = 0; category < 3; category++)
            {
                var page = pages.Single(p => p && p.variant == variant && p.category == category);
                var presentation = page.GetComponent<SettingsPagePresentation>();
                if (!page.stage || !page.close || !presentation || presentation.page != page ||
                    !presentation.resetDefaults || !presentation.scroll || !presentation.helpText ||
                    page.tabs.Length != 3 || page.tabs.Any(b => !b) || page.variants.Length != 3 || page.variants.Any(b => !b) ||
                    page.controls.Length == 0 || page.controls.Any(c => !c.root || !c.root.IsChildOf(page.transform) ||
                        (!c.slider && !c.dropdown) || c.options.Any(b => !b)) || (category == 2 && !page.handPreview))
                    throw new InvalidOperationException("Incomplete page " + PathFor(page));
            }
    }
}
