using System;
using System.Collections.Generic;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 区域规格异步查询端口；只使用调用方捕获的上下文，不在请求中重新读取激活环境。
    /// </summary>
    public interface IRegionSpecSource
    {
        void Fetch(EditorApiContext context, Action<List<RegionSpec>> onDone, Action<string> onError);
    }
}
