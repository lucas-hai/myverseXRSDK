using System;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块上传：POST /api/region/block（Bearer AccessToken），成功即放行本地落盘（保存门禁）。
    /// 本地字段映射：id → tagId；len（长，本地 Z）→ height；width → width。
    /// 注意：后端当前为追加插入——同一 tagId 重复保存会在服务端累积多条记录（API 文档 §五，后端待做去重）。
    /// </summary>
    public sealed class HttpRegionDataUploader : IRegionDataUploader
    {
        [Serializable]
        private class Vec3
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private class BlockRequest
        {
            public string appId;
            public string tagId;
            public float width;
            public float height;
            public Vec3 position;
            public Vec3 rotation;
        }

        public void Upload(EditorApiContext context, LocalRegionData data, Action onDone, Action<string> onError)
        {
            if (context == null || !context.IsRemote || data == null)
            {
                onError?.Invoke("上传需要有效的远端API上下文和地块数据");
                return;
            }
            var serverUrl = context.ServerUrl;
            var appId = context.AppId;
            var accessToken = context.AccessToken;
            var body = JsonUtility.ToJson(new BlockRequest
            {
                appId = appId,
                tagId = data.id,
                width = data.width,
                height = data.len,
                position = new Vec3 { x = data.position.x, y = data.position.y, z = data.position.z },
                rotation = new Vec3 { x = data.rotation.x, y = data.rotation.y, z = data.rotation.z },
            });
            EditorHttp.PostJson($"{serverUrl}/api/region/block", accessToken, body,
                onDone: json =>
                {
                    if (!EditorHttp.TryCheckEnvelope(json, out var envelopeError))
                    {
                        onError?.Invoke(envelopeError);
                        return;
                    }
                    onDone?.Invoke();
                },
                onError: onError);
        }
    }
}
