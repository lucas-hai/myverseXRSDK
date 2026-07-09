using System;
using System.Collections.Generic;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// Mock 规格列表：返回固定假数据，不发网络请求（本地开发不被接口进度阻塞）。
    /// 【临时假数据】正式接入后端规格列表接口时整类删除，换 HttpRegionSpecSource。
    /// </summary>
    public sealed class MockRegionSpecSource : IRegionSpecSource
    {
        public void Fetch(Action<List<RegionSpec>> onDone, Action<string> onError)
        {
            // id 手写字符串模拟远端下发；格式须符合运行时匹配契约（单测有校验）
            onDone?.Invoke(new List<RegionSpec>
            {
                new RegionSpec { id = "12x6", len = 12f, width = 6f },
                new RegionSpec { id = "10x8", len = 10f, width = 8f },
                new RegionSpec { id = "8x8",  len = 8f,  width = 8f },
                new RegionSpec { id = "6x4",  len = 6f,  width = 4f },
            });
        }
    }
}
