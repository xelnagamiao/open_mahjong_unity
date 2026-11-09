using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Salasasa.Venues
{
    public sealed class VenueApplicationResult
    {
        public bool success;
        public string message;
        public JObject data;
    }
    public sealed class VenueApplicationClient : IDisposable
    {
        UnityWebRequest active;
        public void Dispose() { active?.Abort(); active = null; }
        public IEnumerator Submit(VenueApplication value, Action<VenueApplicationResult> complete)
        {
            var user = UserDataManager.Instance;
            int account = user ? user.UserId : 0;
            if (account <= 0 || user.IsTourist || string.IsNullOrEmpty(user.SavedLoginPassword))
            { complete(Failure("请先登录正式账号")); yield break; }
            string origin = ConfigManager.webApiUrl?.TrimEnd('/');
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
            { complete(Failure("服务地址无效")); yield break; }
            VenueApplicationResult auth = null;
            yield return Request(origin + ConfigManager.PlayerLoginPath, new JObject { ["username"] = user.Username, ["password"] = user.SavedLoginPassword }, null, r => auth = r);
            if (!SameAccount(account)) { complete(Failure("账号已切换，请重新打开面板")); yield break; }
            if (auth == null || !auth.success) { complete(auth ?? Failure("身份验证失败")); yield break; }
            string token = (string)auth.data?["token"];
            if ((int?)auth.data?["user_id"] != account || string.IsNullOrEmpty(token))
            { complete(Failure("身份不匹配，请重新登录")); yield break; }
            VenueApplicationResult result = null;
            yield return Request(origin + ConfigManager.PlayerEventApplicationsPath, JObject.FromObject(value), token, r => result = r);
            if (!SameAccount(account)) { complete(Failure("账号已切换，请到个人中心核对申请结果")); yield break; }
            complete(result ?? Failure("请求未确认，请到个人中心核对申请结果"));
        }
        static bool SameAccount(int id) => UserDataManager.Instance && UserDataManager.Instance.UserId == id && !UserDataManager.Instance.IsTourist;
        IEnumerator Request(string url, JObject body, string token, Action<VenueApplicationResult> complete)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                active = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 20;
                request.redirectLimit = 0;
                yield return request.SendWebRequest();
                JObject payload = null;
                try { payload = JObject.Parse(request.downloadHandler.text); } catch (Newtonsoft.Json.JsonException) { }
                bool success = request.result == UnityWebRequest.Result.Success && (bool?)payload?["success"] == true;
                string message = (string)payload?["message"];
                if (string.IsNullOrEmpty(message)) message = success ? "申请已提交" : "请求未确认，请到个人中心核对申请结果，避免重复提交";
                active = null;
                complete(new VenueApplicationResult { success = success, message = message, data = payload?["data"] as JObject });
            }
        }
        static VenueApplicationResult Failure(string message) => new VenueApplicationResult { message = message };
    }
}
