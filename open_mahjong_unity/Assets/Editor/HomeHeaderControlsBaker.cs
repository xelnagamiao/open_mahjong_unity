#if UNITY_EDITOR
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class HomeHeaderControlsBaker
{
    public static void Bake(Scene scene)
    {
        var plans=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        var global=plans.pages[0].transform;
        var home=plans.choices.Single(c=>c.option==DesignOption.Home);
        var buttons=home.buttons.ToList();
        while(buttons.Count<HomeHeaderAppearance.VariantCount)
            buttons.Add(Object.Instantiate(buttons[0],buttons[0].transform.parent));
        home.buttons=buttons.ToArray();
        home.values=HomeHeaderAppearance.DisplayVariantOrder.ToArray();
        for(int i=0;i<home.buttons.Length;i++)
        {
            var button=home.buttons[i];int value=home.values[i];
            button.name="Choice"+value;
            button.onClick=new Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick,plans.SelectHome,value);
            Place((RectTransform)button.transform,122+i%3*142,5+i/3*46,136,40);
            Label(button.GetComponentInChildren<TMP_Text>(true),HomeHeaderAppearance.VariantNames[value]);
        }
        var curtain=plans.choices.FirstOrDefault(c=>c.option==DesignOption.HomeCurtain);
        if(curtain==null)
        {
            var template=plans.choices.Single(c=>c.option==DesignOption.Navigation).buttons[0].transform.parent;
            var root=Object.Instantiate(template,global);root.name="HomeCurtain";
            var all=root.GetComponentsInChildren<Button>(true);
            foreach(var extra in all.Skip(2))Object.DestroyImmediate(extra.gameObject);
            curtain=new DesignChoiceGroup{option=DesignOption.HomeCurtain,buttons=all.Take(2).ToArray(),values=new[]{0,1}};
            plans.choices=plans.choices.Append(curtain).ToArray();
            foreach(var label in root.GetComponentsInChildren<TMP_Text>(true).Where(t=>!t.GetComponentInParent<Button>()))Label(label,"主菜单叠底");
        }
        for(int i=0;i<curtain.buttons.Length;i++)
        {
            var button=curtain.buttons[i];button.name=i==0?"Disabled":"Enabled";
            button.onClick=new Button.ButtonClickedEvent();
            UnityEventTools.AddIntPersistentListener(button.onClick,plans.SelectHomeCurtain,i);
            Place((RectTransform)button.transform,122+i*142,5,136,40);
            Label(button.GetComponentInChildren<TMP_Text>(true),i==0?"关闭":"开启");
        }
        Place((RectTransform)global.Find("Navigation"),24,0,552,66);
        Place((RectTransform)global.Find("Home"),24,66,552,108);
        Place((RectTransform)global.Find("HomeCurtain"),24,174,552,60);
        Place((RectTransform)global.Find("EntrancePalette"),24,234,552,160);
        plans.pageHeights[0]=516;
        plans.presetDefaults.Single(p=>p.option==DesignOption.Home).value=HomeHeaderAppearance.WhiteCornerVariant;
        plans.home.choiceSurfaces=System.Array.Empty<Image>();
        plans.home.BakeDefault();
        EditorUtility.SetDirty(plans.home);
        EditorUtility.SetDirty(plans);
    }
    static void Label(TMP_Text label,string value)
    {
        label.text=value;label.fontStyle=FontStyles.Normal;label.fontWeight=FontWeight.Regular;
        var locale=label.GetComponent<LocalizedPlatformLabel>();if(locale)locale.key=value;
        EditorUtility.SetDirty(label);if(locale)EditorUtility.SetDirty(locale);
    }
    static void Place(RectTransform rect,float x,float y,float width,float height)
    {
        rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.up;
        rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
    }
}
#endif
