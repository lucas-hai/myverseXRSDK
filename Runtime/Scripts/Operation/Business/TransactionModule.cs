using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal class TransactionModule
    {
        // 注意：feeeType 故意保留 4 个 e —— 服务端字段名约定（不要"修正"拼写）
        private class IntegralUseRequest
        {
            [JsonProperty("gameType")] public int GameType;
            [JsonProperty("roomId")] public string RoomId;
            [JsonProperty("gamePackName")] public string GamePackName;
            [JsonProperty("devIds")] public string[] DevIds;
            [JsonProperty("feeeType")] public int FeeeType;
            [JsonProperty("gameDuration")] public int GameDuration;
        }

        private class IntegralUseResponse
        {
            [JsonProperty("code")] public int Code;
        }

        internal void RequestIntegralUse(Action<bool> onComplete)
        {
            var api = string.Format("{0}{1}", MVXRSDK.BaseUrl, MVXRSDKConfig.API_DEDUCTION);
            var req = new IntegralUseRequest
            {
                GameType = 1,
                RoomId = string.Empty,
                GamePackName = Application.identifier,
                DevIds = new[] { MVXRSDK.DeviceId },
                FeeeType = 1,
                GameDuration = 0,
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(req));

            HttpSystem.SendData(api, OnResponse, requestType: HttpRequestType.POST, body);

            void OnResponse(HttpCallBackArgs args)
            {
                if (args.HasError)
                {
                    MVXRSDKLog.Warning($"积分交易验证失败,网络错误");
                    onComplete?.Invoke(false);
                    return;
                }

                IntegralUseResponse resp = null;
                try { resp = JsonConvert.DeserializeObject<IntegralUseResponse>(args.Value); }
                catch (Exception ex) { MVXRSDKLog.Warning($"积分交易响应解析异常: {ex.Message}"); }

                if (resp == null)
                {
                    MVXRSDKLog.Warning("积分交易响应解析失败");
                    onComplete?.Invoke(false);
                    return;
                }

                if (resp.Code != 0)
                {
                    MVXRSDKLog.Warning($"积分交易验证失败,错误码：{resp.Code}");
                    onComplete?.Invoke(false);
                    return;
                }

                MVXRSDKLog.Debug($"积分交易验证成功");
                onComplete?.Invoke(true);
            }
        }
    }
}
