using System;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>异步操作的乐观写入守卫；值快照同时发现列表换位与条目原地修改，不改变既有资产格式。</summary>
    internal sealed class EditorAssetRevision
    {
        private readonly UnityEngine.Object target;
        private readonly string serialized;

        internal EditorAssetRevision(UnityEngine.Object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            this.target = target;
            serialized = JsonUtility.ToJson(target);
        }

        internal bool Matches() => target != null && string.Equals(serialized, JsonUtility.ToJson(target), StringComparison.Ordinal);
    }
}
