using System;
using System.Collections;
using System.Collections.Generic;
using Google.Protobuf;
using Newtonsoft.Json;
using UnityEngine;
using static Login.Types;

namespace MyVerseXRSDK
{
    internal class RoomModule
    {
        // === HTTP 响应 POCO（Newtonsoft.Json 用，v2 PR-9 替代 LitJson）===
        private class IpConfigResponse
        {
            [JsonProperty("code")] public int Code;
            [JsonProperty("data")] public IpConfigData Data;
        }
        private class IpConfigData
        {
            [JsonProperty("baseUrl")] public string BaseUrl;
            [JsonProperty("serveIp")] public string ServeIp;
        }
        private class DeviceAllocResponse
        {
            [JsonProperty("Code")] public int Code;
            [JsonProperty("Data")] public DeviceAllocData Data;
        }
        private class DeviceAllocData
        {
            [JsonProperty("Ip")] public string Ip;
            [JsonProperty("CenterIp")] public string CenterIp;
        }



        private float intvalTime = 1;
        private float currTime;
        private string curIP;
        public string RoomId { get; private set; }
        private string deviceId;


        private string baseUrl;
        private string serverIp;
        /// <summary>
        /// SDK初始化接口，获取设备IP地址
        /// </summary>
        private string deviceApiUrl;
        public void SetDeviceId(string deviceId)
        {
            this.deviceId = deviceId;
        }

        /// <summary>
        /// WsDirect 模式入口：跳过 localhost:8868 HTTP 拉中控地址那一步，
        /// 直接用外部传入的中控服地址设置 deviceApiUrl，启动后续轮询。
        /// 要求：SetDeviceId 已先调（deviceId 用于拼 deviceApiUrl）。
        /// </summary>
        public void SetControlServerDirect(string controlServerAddress)
        {
            serverIp = controlServerAddress; //string.Format("http://{0}:7015", controlServerAddress);
            deviceApiUrl = string.Format("{0}/{1}{2}", serverIp, MVXRSDKConfig.API_DEVEICE_URL, deviceId);
            MVXRSDKLog.Debug($"WsDirect 模式：中控地址={serverIp}，轮询地址={deviceApiUrl}");
        }



        public void RequestIpAddress(string ip, string port, Action<bool, string> cb)
        {
            // 实现获取IP地址的逻辑 
            string ipConfig = string.Format("{0}{1}", ip, port);


            HttpSystem.SendData(ipConfig, OnResponseGetIpConfig, requestType: HttpRequestType.POST);

            MVXRSDKLog.Debug($"初始化SDK，请求IP地址：{ipConfig}");

            void OnResponseGetIpConfig(HttpCallBackArgs args)
            {
                if (args.HasError)
                {
                    MVXRSDKLog.Warning($"初始化SDK失败，网络错误");
                    cb?.Invoke(false, string.Empty);
                    return;
                }

                IpConfigResponse resp = null;
                try { resp = JsonConvert.DeserializeObject<IpConfigResponse>(args.Value); }
                catch (Exception ex) { MVXRSDKLog.Warning($"IpConfig 响应解析异常: {ex.Message}"); }

                if (resp == null || resp.Data == null)
                {
                    MVXRSDKLog.Warning("初始化SDK失败，响应解析失败");
                    cb?.Invoke(false, string.Empty);
                    return;
                }

                if (resp.Code != 0)
                {
                    MVXRSDKLog.Warning($"初始化SDK失败,错误码：{resp.Code}");
                    cb?.Invoke(false, string.Empty);
                    return;
                }

                baseUrl = resp.Data.BaseUrl;
                serverIp = resp.Data.ServeIp;
                MVXRSDKLog.Debug($"中控地址：{serverIp}，总控地址：{baseUrl}");
                deviceApiUrl = string.Format("{0}/{1}{2}", serverIp, MVXRSDKConfig.API_DEVEICE_URL, deviceId);
                cb?.Invoke(true, baseUrl);
                MVXRSDKLog.Debug($"设备ID：{deviceId},初始化SDK成功");
            }

        }


        public void Update()
        {
            if (string.IsNullOrEmpty(deviceApiUrl)) return;
            currTime += Time.deltaTime;
            if (currTime >= intvalTime)
            {
                currTime = 0;
                HttpSystem.SendData(deviceApiUrl, OnResponse, isLog: false);
            }

        }

        private void OnResponse(HttpCallBackArgs args)
        {
            if (args.HasError)
            {
                if (MVXRSDK.RoomAllocationStatus == RoomAllocationStatus.Undistributed) return;
                MVXRSDK.SetRoomAllocationStatus(RoomAllocationStatus.Undistributed);
                return;
            }

            DeviceAllocResponse resp = null;
            try { resp = JsonConvert.DeserializeObject<DeviceAllocResponse>(args.Value); }
            catch (Exception ex) { MVXRSDKLog.Warning($"DeviceAlloc 响应解析异常: {ex.Message}"); }

            if (resp == null)
            {
                MVXRSDKLog.Error("房间分配响应解析失败");
                return;
            }

            if (resp.Code != 0)
            {
                MVXRSDKLog.Error($"房间分配失败,错误码：{resp.Code}");
                return;
            }

            string ip = resp.Data?.CenterIp;

            // 与服务器约定 IP 值为空时，清空数据，设备进入待分配状态
            if (string.IsNullOrEmpty(ip))
            {
                if (!string.IsNullOrEmpty(curIP))
                {
                    // 房间解散
                    curIP = ip;
                    EventSystem.EventTrigger(MVXRSDKEventType.ROOM_DISBAND);
                    MVXRSDKLog.Debug($"房间解散");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(curIP)) return;
                curIP = ip;
                MVXRSDKLog.Debug($"房间分配成功");
                EventSystem.EventTrigger(MVXRSDKEventType.ROOM_ALLOCATE_SUCCESS, ip);
            }
        }







    }
}