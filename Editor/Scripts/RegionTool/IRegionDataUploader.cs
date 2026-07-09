using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块数据上传抽象：上传成功是本地落盘的前置条件（保存门禁）。
    /// Http 实现待远端接口定义后补（HttpRegionDataUploader 占位，请求携带 SdkAuthStore 凭据），
    /// 切换点在 <see cref="RegionToolServices.Uploader"/>。删除条目暂不同步远端（需求未提）。
    /// </summary>
    public interface IRegionDataUploader
    {
        void Upload(LocalRegionData data, Action onDone, Action<string> onError);
    }
}
