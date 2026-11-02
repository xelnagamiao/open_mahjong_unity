using System.Linq;

namespace Salasasa.Events
{
    public sealed partial class EventVenuePanel
    {
        string ReadLocalized(string key,int index)
        {
            string source=Read(key,index);
            switch(key){
                case "entryFlag":return ConfigManager.CurrentLanguage==AppLanguage.English?(source=="开启"?"On":"Off"):PlatformText.Get(source);
                case "title":return string.IsNullOrEmpty(detail?.name)?PlatformText.Get(source):source;
                case "description":return string.IsNullOrEmpty(detail?.description)?PlatformText.Get(source):source;
                case "noticeListTitle":case "noticeListMeta":case "noticeTitle":case "noticeBody":case "noticeMeta":
                case "reviewName":case "reviewUid":case "reviewRemark":case "adminRow":case "statPlayerName":return source;
                case "presetName":return seatPresetIndex==0?PlatformText.Get(source):source;
                case "presetListName":return index==0?PlatformText.Get(source):source;
                case "reviewCount":return PlatformText.Format("报名记录  {0}",Reviews().Count);
                case "queueCount":return PlatformText.Format("已选 {0} / 4",selectedReady.Count);
                case "queueName":
                    var player=ready.ElementAtOrDefault(index);
                    return Str(player,"username")+"     UID "+Str(player,"user_id")+(selectedReady.Contains(Num(player,"user_id"))?"     "+PlatformText.Get("已选择"):"");
                case "profileStatus":
                    if(profile?["last_rejected"]?.Type==Newtonsoft.Json.Linq.JTokenType.Object&&profile?["pending"]?.Type!=Newtonsoft.Json.Linq.JTokenType.Object)
                        return PlatformText.Get("审核未通过\n")+Str(profile["last_rejected"],"review_note");
                    break;
            }
            return PlatformText.Get(source);
        }
    }
}
