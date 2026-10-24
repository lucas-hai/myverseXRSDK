using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块上传端口；一次操作的所有上传必须使用同一捕获上下文。成功只表示远端接受，不直接写本地资产。
    /// </summary>
    public interface IRegionDataUploader
    {
        void Upload(EditorApiContext context, LocalRegionData data, Action onDone, Action<string> onError);
    }
}
