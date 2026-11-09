using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Presentation-only translations. Never use translated strings as protocol or option values.</summary>
public static partial class PlatformText
{
    [Serializable] public sealed class Entry { public string key, zh, tw, en; }
    [Serializable] sealed class Catalog { public Entry[] entries; public string traditionalCharacters; }
    sealed class Template { public Regex expression; public string english; public string[] tokens; }
    static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);
    static readonly Regex Placeholders = new Regex(@"\{(?:[A-Za-z_][^{}\r\n]*|\d+(?::[^{}\r\n]+)?)\}", RegexOptions.CultureInvariant);
    static readonly Regex Tags = new Regex(@"(<[^>]+>)", RegexOptions.CultureInvariant);
    static readonly Regex Parts = new Regex(@"(\r?\n|\s*[·/|]\s*|：|；|[（）()]|(?<=\S)\s{2,}(?=\S))", RegexOptions.CultureInvariant);
    static readonly Regex Multiplicity = new Regex(@"^(?<name>.+?)\s*(?<mark>[*×])\s*(?<count>\d+)$", RegexOptions.CultureInvariant);
    static readonly Regex NumberUnit = new Regex(@"^([+−\-]?[\d,.]+)\s*(人|名|场|局|副|番|符|点|分|张|件|秒|分钟|级|段|页|条|底)$", RegexOptions.CultureInvariant);
    static readonly Dictionary<string,string> Units = new Dictionary<string,string> {
        {"人","players"},{"名","players"},{"场","matches"},{"局","hands"},{"副","deals"},{"番","fan"},{"符","fu"},
        {"点","points"},{"分","points"},{"张","cards"},{"件","items"},{"秒","s"},{"分钟","min"},{"级","kyu"},
        {"段","dan"},{"页","pages"},{"条","entries"},{"底","base"}
    };
    static Dictionary<string,Entry> entries;
    static Dictionary<string,Entry> normalized;
    static Dictionary<char,char> traditional;
    static List<Template> templates;
    static readonly Dictionary<string,string> englishCache = new Dictionary<string,string>(StringComparer.Ordinal);
    static void Load()
    {
        if(entries!=null)return;
        entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        normalized=new Dictionary<string,Entry>(StringComparer.Ordinal);
        traditional=new Dictionary<char,char>();templates=new List<Template>();
        var asset=Resources.Load<TextAsset>("Localization/PlatformUI");
        var catalog=asset?JsonUtility.FromJson<Catalog>(asset.text):null;
        foreach(var entry in catalog?.entries??Array.Empty<Entry>()){
            entries[entry.key]=entry;
            normalized[Normalize(entry.key)]=entry;
        }
        string pairs=catalog?.traditionalCharacters??"";
        for(int i=0;i+1<pairs.Length;i+=2)traditional[pairs[i]]=pairs[i+1];
        // Literal wording determines specificity; placeholder names must not make
        // "{count}巡" take precedence over "第{0}巡".
        foreach(var entry in entries.Values.OrderByDescending(e=>Placeholders.Replace(e.key, "").Length).ThenByDescending(e=>e.key.Length)){
            var matches=Placeholders.Matches(entry.key);
            if(matches.Count==0||!HasChinese(entry.key))continue;
            var pattern=new StringBuilder("^");int offset=0;
            var tokens=new string[matches.Count];
            for(int i=0;i<matches.Count;i++){
                var m=matches[i];pattern.Append(Regex.Escape(entry.key.Substring(offset,m.Index-offset)));
                bool numericSuffix=matches.Count==1&&Regex.IsMatch(entry.key,@"^\{[^}]+\}\s*[位名级段人场局副番符点分张件秒页条圈巡]$");
                pattern.Append("(?<v").Append(i).Append(numericSuffix?">[+−\\-]?[\\d,.]+)":">.+?)");tokens[i]=m.Value;offset=m.Index+m.Length;
            }
            pattern.Append(Regex.Escape(entry.key.Substring(offset))).Append('$');
            templates.Add(new Template{expression=new Regex(pattern.ToString(),RegexOptions.CultureInvariant|RegexOptions.Singleline),english=entry.en,tokens=tokens});
        }
    }
    static string Normalize(string text)=>Spaces.Replace(text??""," ").Trim();
    public static bool HasChinese(string text)=>!string.IsNullOrEmpty(text)&&text.Any(c=>c>=0x3400&&c<=0x9fff);
    public static bool Contains(string key){Load();return key!=null&&(entries.ContainsKey(key)||normalized.ContainsKey(Normalize(key)));}
    public static string Get(string key)=>Get(key,ConfigManager.CurrentLanguage);
    public static string Get(string key,AppLanguage language)
    {
        if(string.IsNullOrEmpty(key))return key??"";
        if(HasLocaleCatalog(language))return GetLocale(key,language);
        Load();
        if(entries.TryGetValue(key,out var entry)||normalized.TryGetValue(Normalize(key),out entry))
            return language==AppLanguage.English?entry.en:language==AppLanguage.TraditionalChinese?entry.tw:entry.zh;
        if(language==AppLanguage.SimplifiedChinese||!HasChinese(key))return key;
        if(language==AppLanguage.TraditionalChinese){var chars=key.ToCharArray();for(int i=0;i<chars.Length;i++)if(traditional.TryGetValue(chars[i],out var c))chars[i]=c;return new string(chars);}
        if(englishCache.TryGetValue(key,out var cached))return cached;
        var segments=Tags.Split(key);
        for(int i=0;i<segments.Length;i++)if(!segments[i].StartsWith("<",StringComparison.Ordinal))segments[i]=English(segments[i],true);
        string result=Regex.Replace(string.Concat(segments),@"(?<=\))(?=[A-Za-z])"," ");
        if(englishCache.Count>4096)englishCache.Clear();englishCache[key]=result;return result;
    }
    static string English(string source,bool split)
    {
        if(!HasChinese(source))return source;
        if(entries.TryGetValue(source,out var entry))return entry.en;
        string trimmed=source.Trim();
        if(normalized.TryGetValue(Normalize(source),out entry)){
            int start=source.IndexOf(trimmed,StringComparison.Ordinal);
            return source.Substring(0,start)+entry.en+source.Substring(start+trimmed.Length);
        }
        var round=Regex.Match(trimmed,@"^(?<round>(?:[东南西北]风[东南西北]|[东南西北][一二三四0-9]+局))\s+(?<honba>\d+)本场$");
        if(round.Success)return English(round.Groups["round"].Value,true)+" · "+round.Groups["honba"].Value+"H";
        foreach(var template in templates){
            var match=template.expression.Match(source);if(!match.Success)continue;
            string value=template.english;
            for(int i=0;i<template.tokens.Length;i++)value=value.Replace(template.tokens[i],match.Groups["v"+i].Value);
            return value;
        }
        var repeated=Multiplicity.Match(trimmed);
        if(repeated.Success){
            string name=repeated.Groups["name"].Value;
            string translated=English(name,true);
            if(translated!=name)return translated+" ×"+repeated.Groups["count"].Value;
        }
        var number=NumberUnit.Match(trimmed);
        if(number.Success)return number.Groups[1].Value+" "+Units[number.Groups[2].Value];
        var rank=Regex.Match(trimmed,@"^第\s*(\d+)\s*名$");
        if(rank.Success)return "#"+rank.Groups[1].Value;
        if(split && source.IndexOf('\n')>=0){
            var lines=Regex.Split(source,@"(\r?\n)");
            for(int i=0;i<lines.Length;i+=2)lines[i]=English(lines[i],true);
            return string.Concat(lines);
        }
        if(split){
            var parts=Parts.Split(source);
            if(parts.Length>1){for(int i=0;i<parts.Length;i++)parts[i]=parts[i]=="："?": ":parts[i]=="；"?"; ":parts[i]=="（"?" (":parts[i]=="）"?")":English(parts[i],false);return string.Concat(parts);}
        }
        return source;
    }
    public static string Format(string key,params object[] values)=>string.Format(CultureInfo.InvariantCulture,Get(key),values);
}
