using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Salasasa.Events
{
    public sealed class EventAdminResult
    {
        public bool Success;
        public long Status;
        public string Message;
        public JObject Body;
        public JToken Data=>Body?["data"];
    }

    public sealed class EventAdminClient : IDisposable
    {
        string token,origin;
        int identity;
        DateTime expires;
        UnityWebRequest active;
        public void Dispose(){active?.Abort();active=null;token=null;identity=0;}
        public IEnumerator Send(string method,string path,JObject body,Action<EventAdminResult> complete)
        {
            var user=UserDataManager.Instance;
            int account=user?user.UserId:0;
            string baseUrl=ConfigManager.webApiUrl?.TrimEnd('/');
            if(account<=0 || user.IsTourist){complete(Failure("请先登录正式账号"));yield break;}
            if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri)||!(uri.Scheme=="https" || uri.Scheme=="http"&&uri.IsLoopback))
            {complete(Failure("管理服务地址无效"));yield break;}
            if(identity!=account||origin!=baseUrl||DateTime.UtcNow>=expires){token=null;identity=account;origin=baseUrl;}
            if(string.IsNullOrEmpty(token))
            {
                if(string.IsNullOrEmpty(user.SavedLoginPassword)){complete(Failure("管理身份已失效，请重新登录后重试"));yield break;}
                EventAdminResult login=null;
                yield return Request(baseUrl+"/api/event-admin/auth/login","POST",new JObject{["username"]=user.Username,["password"]=user.SavedLoginPassword},false,r=>login=r);
                if(!SameUser(account)){complete(Failure("账号已切换，请重新打开面板"));yield break;}
                if(login==null||!login.Success){complete(login??Failure("管理身份验证失败"));yield break;}
                if((int?)login.Data?["user_id"]!=account){token=null;complete(Failure("管理身份不匹配，请重新登录"));yield break;}
                token=(string)login.Data?["token"];
                if(string.IsNullOrEmpty(token)){complete(Failure("管理身份验证失败"));yield break;}
                expires=DateTime.UtcNow.AddSeconds(Math.Max(30,(int?)login.Data?["expires_in"]??3600)-15);
            }
            EventAdminResult result=null;
            yield return Request(baseUrl+"/api/event-admin"+path,method,body,true,r=>result=r);
            if(!SameUser(account)){token=null;complete(Failure("账号已切换，请重新打开面板"));yield break;}
            if(result?.Status==401){token=null;result.Message="管理身份已过期，请刷新后重试";}
            complete(result??Failure("请求未完成，请刷新后核对结果"));
        }
        static bool SameUser(int id)=>UserDataManager.Instance && UserDataManager.Instance.UserId==id && !UserDataManager.Instance.IsTourist;
        IEnumerator Request(string url,string method,JObject body,bool authorized,Action<EventAdminResult> complete)
        {
            using(var request=new UnityWebRequest(url,method))
            {
                active=request;request.downloadHandler=new DownloadHandlerBuffer();request.timeout=20;request.redirectLimit=0;
                request.SetRequestHeader("Accept","application/json");
                if(authorized)request.SetRequestHeader("Authorization","Bearer "+token);
                if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));request.SetRequestHeader("Content-Type","application/json");}
                yield return request.SendWebRequest();
                JObject parsed=null;
                try{parsed=JObject.Parse(request.downloadHandler.text);}catch(Newtonsoft.Json.JsonException){}
                bool success=request.result==UnityWebRequest.Result.Success && (bool?)parsed?["success"]==true;
                string message=(string)parsed?["message"];
                if(string.IsNullOrEmpty(message))message=success?"操作成功":method=="GET"?"加载失败，请检查连接后重试":"请求未确认，请刷新后核对结果，避免重复操作";
                active=null;complete(new EventAdminResult{Success=success,Status=request.responseCode,Body=parsed,Message=message});
            }
        }
        static EventAdminResult Failure(string message)=>new EventAdminResult{Message=message};
    }
}
