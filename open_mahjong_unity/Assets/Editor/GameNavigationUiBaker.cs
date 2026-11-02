#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using Salasasa.Leaderboards;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameNavigationUiBaker
{
    public static void Bake(Scene scene)
    {
        ChatUiBaker.BakeSinglePreview(scene);
        var plans=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        LeaderboardPresentationBaker.Bake(scene);
        RemoveChoice(plans, DesignOption.SettingsHint);
        plans.settings.SelectHintStyle(0);
        foreach(var preset in plans.presetDefaults.Where(p=>p.option==DesignOption.SettingsHint))preset.value=0;
        ReflowPage(plans);
        EditorUtility.SetDirty(plans);
    }
    static void RemoveChoice(InterfacePlansController plans, DesignOption option)
    {
        var group=plans.choices.SingleOrDefault(c=>c.option==option);
        if(group==null)return;
        var row=group.buttons.FirstOrDefault(b=>b)?.transform.parent;
        plans.choices=plans.choices.Where(c=>c!=group).ToArray();
        if(row)UnityEngine.Object.DestroyImmediate(row.gameObject);
    }
    static void ReflowPage(InterfacePlansController plans)
    {
        var page=plans.pages[1].transform;
        var rows=plans.choices.Where(g=>g.buttons.Length>0&&g.buttons[0])
            .Select(g=>g.buttons[0].transform.parent as RectTransform)
            .Where(r=>r&&r.parent==page&&r.gameObject.activeSelf).Distinct()
            .OrderByDescending(r=>r.anchoredPosition.y).ThenBy(r=>r.GetSiblingIndex()).ToArray();
        float top=0;
        foreach(var row in rows)
        {
            if(row.name=="MailPalette")top+=8;
            Place(row,24,top,552,row.sizeDelta.y);
            MoveDivider(row);
            top+=row.sizeDelta.y;
        }
        plans.pageHeights[1]=top+128;
    }
    static void MoveDivider(RectTransform row)
    {
        foreach(RectTransform child in row)
            if(child.rect.height<=2&&child.GetComponent<UnityEngine.UI.Graphic>())
                child.anchoredPosition=new Vector2(child.anchoredPosition.x,-row.sizeDelta.y+2);
    }
    static void Place(RectTransform rect,float left,float top,float width,float height)
    {
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(left,-top);rect.sizeDelta=new Vector2(width,height);
    }
}
#endif
