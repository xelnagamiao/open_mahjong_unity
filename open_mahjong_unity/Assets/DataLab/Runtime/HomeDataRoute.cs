using System;
using System.Collections.Generic;
using UnityEngine;
namespace Salasasa.DataLab
{
    public sealed class HomeDataRoute:MonoBehaviour
    {
        public HomeDataPanel panel;
        public bool HandleRoute(string route)
        {
            if(!Uri.TryCreate(route,UriKind.Absolute,out var uri)||uri.Scheme!="salasasa"||uri.Host!="data")return false;
            var args=new Dictionary<string,string>();
            foreach(var pair in uri.Query.TrimStart('?').Split('&')){var part=pair.Split(new[]{'='},2);if(part.Length==2)args[Uri.UnescapeDataString(part[0])]=Uri.UnescapeDataString(part[1]);}
            panel.OpenPlatform(args.TryGetValue("rule",out var rule)?rule:"guobiao",args.TryGetValue("scope",out var scope)?scope:"beginner");
            if(args.TryGetValue("style",out var style)&&int.TryParse(style,out var i))panel.SetStyle(i);
            panel.SetPage(args.TryGetValue("view",out var view)&&view=="recent"?1:0);return true;
        }
    }
}
