using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 区域规格列表：GET /api/region/list?appId=...（Bearer AccessToken）。
    /// 远端字段映射：tagId → RegionSpec.id（原样保留，运行时匹配键）；
    /// height → len（长，本地 Z）；width → width（宽，本地 X）。
    /// </summary>
    public sealed class HttpRegionSpecSource : IRegionSpecSource
    {
        [Serializable]
        private class ListEnvelope
        {
            public int code;
            public string message;
            public ListData data;
        }

        [Serializable]
        private class ListData
        {
            public List<RemoteRegion> regions;
        }

        [Serializable]
        private class RemoteRegion
        {
            public string tagId;
            public float width;
            public float height;
        }

        public void Fetch(EditorApiContext context, Action<List<RegionSpec>> onDone, Action<string> onError)
        {
            if (context == null || !context.IsRemote)
            {
                onError?.Invoke("规格查询需要有效的远端API上下文");
                return;
            }
            var serverUrl = context.ServerUrl;
            var appId = context.AppId;
            var accessToken = context.AccessToken;
            var url = $"{serverUrl}/api/region/list?appId={UnityWebRequest.EscapeURL(appId)}";
            EditorHttp.Get(url, accessToken,
                onDone: json =>
                {
                    if (!EditorHttp.TryCheckEnvelope(json, out var envelopeError))
                    {
                        onError?.Invoke(envelopeError);
                        return;
                    }
                    var envelope = JsonUtility.FromJson<ListEnvelope>(json);
                    var specs = new List<RegionSpec>();
                    if (envelope.data != null && envelope.data.regions != null)
                    {
                        foreach (var region in envelope.data.regions)
                        {
                            if (string.IsNullOrEmpty(region.tagId)) continue;
                            specs.Add(new RegionSpec { id = region.tagId, len = region.height, width = region.width });
                        }
                    }
                    onDone?.Invoke(specs);
                },
                onError: onError);
        }
    }
}
