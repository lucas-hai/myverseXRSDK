using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>中控导演请求的Inspector配置；不包含本地采集、编码参数或WHIP地址。</summary>
    [Serializable]
    public sealed class DirectorRequestSettings
    {
        [Tooltip("Source：unity使用本地独立相机；留空或mr申请原直播，其他值按中控约定原样发送。")]
        public string source = "unity";
        [Min(1), Tooltip("Lenses：申请镜头数，至少1；不代表本地创建多台相机。")]
        public int lenses = 1;
        [Min(1), Tooltip("DurationSec：申请时长（秒），必须大于0；实际切换和开停由中控决定。")]
        public int durationSeconds = 10;
        [Tooltip("Record：请求中控录制；不启动本地录屏，也不代表录制文件已生成。")]
        public bool record;
        [Tooltip("FileName：仅Record开启时有意义；协议约定填写不含扩展名的录制名称（如business-record）。SDK原样透传，不增删后缀；不是本地保存路径。")]
        public string fileName = "";

        internal DirectorRequest CreateRequest(Camera unityCamera)
        {
            return new DirectorRequest
            {
                Source = source,
                Lenses = lenses,
                DurationSeconds = durationSeconds,
                Record = record,
                FileName = fileName,
                // 仅unity附本地候选，避免SDK把原直播的空Source自动补成unity。
                AutoCamera = source == "unity" ? unityCamera : null
            };
        }
    }
}
