using System;
using System.Collections.Generic;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 区域规格列表来源抽象。Http 实现待远端接口定义后补（HttpRegionSpecSource 占位，
    /// UnityWebRequest + EditorApplication.update 异步驱动，请求携带 SdkAuthStore 凭据），
    /// 切换点在 <see cref="RegionToolServices.SpecSource"/>。
    /// </summary>
    public interface IRegionSpecSource
    {
        void Fetch(Action<List<RegionSpec>> onDone, Action<string> onError);
    }
}
